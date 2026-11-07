using System.Diagnostics;
using System.Security.Cryptography;
using SystemMate.Models;

namespace SystemMate.Services;

/// <summary>Read-only, bounded folder analysis. File sizes are logical bytes, not allocated disk space.</summary>
public sealed class StorageAnalysisService
{
    public Task<StorageAnalysisResult> AnalyzeAsync(string rootPath, StorageAnalysisOptions? options = null,
        IProgress<StorageAnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        options ??= new StorageAnalysisOptions();
        if (options.MaximumFiles < 1 || options.MaximumEntries < 1 || options.MaximumDepth < 1
            || options.MaximumDuplicateGroups < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Analysis limits must be positive.");

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        return Task.Run(() => AnalyzeCoreAsync(root, options, progress, cancellationToken), cancellationToken);
    }

    private static async Task<StorageAnalysisResult> AnalyzeCoreAsync(string root,
        StorageAnalysisOptions options, IProgress<StorageAnalysisProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The selected folder no longer exists.");
        if (HasReparsePointInPath(root))
            throw new IOException("Choose a regular folder. Linked folders and junctions are not scanned.");

        var files = new List<StorageFile>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        long totalBytes = 0;
        int skipped = 0, entries = 0;
        bool partial = false;
        var clock = Stopwatch.StartNew();

        void Report(string stage, string path, bool force = false)
        {
            if (!force && clock.ElapsedMilliseconds < 150) return;
            progress?.Report(new StorageAnalysisProgress(stage, files.Count, totalBytes, path));
            clock.Restart();
        }

        while (pending.Count > 0 && !partial)
        {
            token.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Pop();
            try
            {
                // Recheck immediately before enumeration in case a directory changed after discovery.
                if (HasReparsePointInPath(directory)) { skipped++; continue; }
                using var enumerator = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
                while (enumerator.MoveNext())
                {
                    token.ThrowIfCancellationRequested();
                    if (++entries > options.MaximumEntries) { partial = true; break; }
                    var path = enumerator.Current;
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0 || HasReparsePointInPath(path))
                        { skipped++; continue; }
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (depth >= options.MaximumDepth) { skipped++; partial = true; continue; }
                            pending.Push((path, depth + 1));
                            continue;
                        }
                        if (files.Count >= options.MaximumFiles) { partial = true; break; }
                        var info = new FileInfo(path);
                        var item = new StorageFile(info.FullName, info.Length, info.LastWriteTimeUtc);
                        files.Add(item);
                        totalBytes += item.SizeBytes;
                        Report("Measuring files", path);
                    }
                    catch (Exception exception) when (IsFileAccessError(exception)) { skipped++; }
                }
            }
            catch (Exception exception) when (IsFileAccessError(exception)) { skipped++; }
        }

        var duplicates = new List<StorageDuplicateGroup>();
        bool duplicateLimit = false;
        if (options.FindDuplicates)
        {
            Report("Comparing file contents", root, force: true);
            foreach (var candidates in files.Where(file => file.SizeBytes > 0)
                         .GroupBy(file => file.SizeBytes).Where(group => group.Count() > 1))
            {
                token.ThrowIfCancellationRequested();
                var hashes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var file in candidates)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if (HasReparsePointInPath(file.FullPath)) { skipped++; continue; }
                        var info = new FileInfo(file.FullPath);
                        if (info.Length != file.SizeBytes || info.LastWriteTimeUtc != file.LastWriteTimeUtc)
                        { skipped++; continue; }
                        Report("Comparing file contents", file.FullPath);
                        await using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read,
                            FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        if (stream.Length != file.SizeBytes || HasReparsePointInPath(file.FullPath))
                        { skipped++; continue; }
                        var digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                        info.Refresh();
                        if (info.Length != file.SizeBytes || info.LastWriteTimeUtc != file.LastWriteTimeUtc)
                        { skipped++; continue; }
                        if (!hashes.TryGetValue(digest, out var paths)) hashes[digest] = paths = new();
                        paths.Add(file.FullPath);
                    }
                    catch (Exception exception) when (IsFileAccessError(exception)) { skipped++; }
                }
                foreach (var match in hashes.Where(pair => pair.Value.Count > 1))
                {
                    if (duplicates.Count >= options.MaximumDuplicateGroups) { duplicateLimit = true; break; }
                    duplicates.Add(new StorageDuplicateGroup(match.Key, candidates.Key, match.Value.AsReadOnly()));
                }
                if (duplicateLimit) break;
            }
        }

        token.ThrowIfCancellationRequested();
        var result = new StorageAnalysisResult
        {
            RootPath = root,
            FileCount = files.Count,
            TotalBytes = totalBytes,
            SkippedEntries = skipped,
            IsPartial = partial,
            DuplicateGroupsLimited = duplicateLimit,
            DuplicatesChecked = options.FindDuplicates,
            LargestFiles = files.OrderByDescending(file => file.SizeBytes).Take(100).ToArray(),
            FileTypes = Summarize(files, file => string.IsNullOrEmpty(Path.GetExtension(file.FullPath))
                ? "No extension" : Path.GetExtension(file.FullPath).ToLowerInvariant()).Take(100).ToArray(),
            Folders = Summarize(files, file =>
            {
                var relative = Path.GetRelativePath(root, file.FullPath);
                var split = relative.IndexOf(Path.DirectorySeparatorChar);
                return split < 0 ? "Files in selected folder" : relative[..split];
            }).Take(100).ToArray(),
            DuplicateGroups = duplicates.OrderByDescending(group => group.DuplicateCopyBytes).ToArray()
        };
        Report("Complete", root, force: true);
        return result;
    }

    private static IEnumerable<StorageBreakdown> Summarize(IEnumerable<StorageFile> files,
        Func<StorageFile, string> keySelector) => files.GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
        .Select(group => new StorageBreakdown(group.Key, group.Sum(file => file.SizeBytes), group.Count()))
        .OrderByDescending(group => group.SizeBytes);

    private static bool HasReparsePointInPath(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }

    private static bool IsFileAccessError(Exception exception) => exception is IOException
        or UnauthorizedAccessException or System.Security.SecurityException;
}

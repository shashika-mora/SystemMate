using System.Diagnostics;
using SystemMate.Models;

namespace SystemMate.Services;

/// <summary>Scans allowlisted cache locations without changing them; revalidates each file before cleanup.</summary>
public sealed class CleanerService
{
    private readonly HistoryService _history;
    private readonly string _localAppData;
    private readonly string _roamingAppData;
    private readonly string[] _temporaryDirectories;
    private readonly Func<string, bool> _isProcessRunning;

    public CleanerService(HistoryService history)
        : this(history,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            [Path.GetTempPath(), Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? "C:\\Windows", "Temp"),
             Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")],
            IsProcessRunning)
    {
    }

    // Isolated locations allow regression tests to exercise the real rules without scanning personal files.
    internal CleanerService(HistoryService history, string localAppData, string roamingAppData,
        IEnumerable<string> temporaryDirectories, Func<string, bool> isProcessRunning)
    {
        _history = history;
        _localAppData = localAppData;
        _roamingAppData = roamingAppData;
        _temporaryDirectories = temporaryDirectories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _isProcessRunning = isProcessRunning;
    }

    public IReadOnlyList<CleanupCategory> GetCategories() =>
    [
        new() { Id = "win_temp", DisplayName = "Temporary files", Description = "User and Windows temporary folders; inaccessible files are skipped." },
        new() { Id = "browser_cache", DisplayName = "Browser caches", Description = "Chrome, Edge, Brave, Vivaldi and Firefox caches. Cookies, history and passwords are excluded." },
        new() { Id = "app_cache", DisplayName = "Application caches", Description = "VS Code cached data, npm content cache and pip cache." },
        new() { Id = "thumbnails", DisplayName = "Thumbnail cache", Description = "Only thumbcache_*.db files; other Explorer data is excluded." }
    ];

    public async Task<ScanResult> ScanAsync(IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ScanResult();
        result.Categories.AddRange(GetCategories());
        await Task.Run(() =>
        {
            var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in result.Categories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Scanning {category.DisplayName}…");
                var notes = new HashSet<string>();
                foreach (var target in GetTargets(category.Id))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (OwnerIsRunning(target))
                    {
                        notes.Add($"Close {target.ProcessName} and scan again to include its cache.");
                        continue;
                    }
                    foreach (var path in EnumerateFilesSafely(target.Directory, !target.ThumbnailOnly, cancellationToken, notes))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            var fullPath = Path.GetFullPath(path);
                            if (!IsSafeCandidate(fullPath, target) || !seenFiles.Add(fullPath)) continue;
                            var info = new FileInfo(fullPath);
                            category.FilePaths.Add(fullPath);
                            category.TotalSizeBytes += info.Length;
                            category.FileCount++;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            notes.Add("Some inaccessible or changing files were skipped.");
                        }
                    }
                }
                category.ScanNote = string.Join(" ", notes);
            }
        }, cancellationToken);
        return result;
    }

    private sealed record CleanupTarget(string Directory, string? ProcessName = null, bool ThumbnailOnly = false);

    private IReadOnlyList<CleanupTarget> GetTargets(string categoryId)
    {
        IEnumerable<CleanupTarget> targets = categoryId switch
        {
            "win_temp" => _temporaryDirectories.Select(path => new CleanupTarget(path)),
            "browser_cache" => GetBrowserTargets(),
            "app_cache" => new[]
            {
                new CleanupTarget(Path.Combine(_roamingAppData, "Code", "Cache"), "Code"),
                new CleanupTarget(Path.Combine(_roamingAppData, "Code", "CachedData"), "Code"),
                new CleanupTarget(Path.Combine(_roamingAppData, "Code", "Code Cache"), "Code"),
                new CleanupTarget(Path.Combine(_localAppData, "npm-cache", "_cacache")),
                new CleanupTarget(Path.Combine(_roamingAppData, "npm-cache", "_cacache")),
                new CleanupTarget(Path.Combine(_localAppData, "pip", "Cache"))
            },
            "thumbnails" => new[] { new CleanupTarget(Path.Combine(_localAppData, "Microsoft", "Windows", "Explorer"), ThumbnailOnly: true) },
            _ => Array.Empty<CleanupTarget>()
        };
        return targets.DistinctBy(target => Path.GetFullPath(target.Directory), StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IEnumerable<CleanupTarget> GetBrowserTargets()
    {
        var browsers = new[]
        {
            (Path.Combine(_localAppData, "Google", "Chrome", "User Data"), "chrome"),
            (Path.Combine(_localAppData, "Microsoft", "Edge", "User Data"), "msedge"),
            (Path.Combine(_localAppData, "BraveSoftware", "Brave-Browser", "User Data"), "brave"),
            (Path.Combine(_localAppData, "Vivaldi", "User Data"), "vivaldi")
        };
        foreach (var (userData, process) in browsers)
        {
            foreach (var profile in GetChildDirectories(userData))
            {
                var name = Path.GetFileName(profile);
                if (name != "Default" && name != "Guest Profile" && !name.StartsWith("Profile ", StringComparison.Ordinal)) continue;
                foreach (var cache in new[] { "Cache", "Code Cache", "GPUCache" })
                    yield return new CleanupTarget(Path.Combine(profile, cache), process);
            }
        }
        foreach (var profile in GetChildDirectories(Path.Combine(_localAppData, "Mozilla", "Firefox", "Profiles")))
            yield return new CleanupTarget(Path.Combine(profile, "cache2"), "firefox");
    }

    private static IEnumerable<string> GetChildDirectories(string root)
    {
        try
        {
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                return Array.Empty<string>();
            return Directory.GetDirectories(root).Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private bool OwnerIsRunning(CleanupTarget target)
    {
        if (target.ProcessName is null) return false;
        try { return _isProcessRunning(target.ProcessName); }
        catch { return true; } // An unknown ownership check must not authorize cache removal.
    }

    private static bool IsProcessRunning(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static IEnumerable<string> EnumerateFilesSafely(string root, bool recurse,
        CancellationToken cancellationToken, ISet<string> notes)
    {
        var pending = new Stack<string>();
        if (Directory.Exists(root)) pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            var files = new List<string>();
            try
            {
                var info = new DirectoryInfo(directory);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var file in info.EnumerateFiles())
                    if ((file.Attributes & FileAttributes.ReparsePoint) == 0) files.Add(file.FullName);
                if (recurse)
                    foreach (var child in info.EnumerateDirectories())
                        if ((child.Attributes & FileAttributes.ReparsePoint) == 0) pending.Push(child.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                notes.Add("Some inaccessible folders were skipped.");
            }
            foreach (var file in files) yield return file;
        }
    }

    public async Task<CleanupSession> CleanAsync(ScanResult scanResult,
        IProgress<(string file, int done, int total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var session = new CleanupSession { SessionId = Guid.NewGuid().ToString("N"), StartedAt = DateTime.Now };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = scanResult.Categories.Where(c => c.IsSelected)
            .SelectMany(c => c.FilePaths.Select(path => (Path: path, Category: c)))
            .Where(item => seen.Add(NormalizeCandidatePath(item.Path))).ToList();
        await Task.Run(() =>
        {
            var done = 0;
            foreach (var (path, category) in files)
            {
                if (cancellationToken.IsCancellationRequested) { session.WasCanceled = true; break; }
                progress?.Report((path, ++done, files.Count));
                var record = new OperationRecord { SessionId = session.SessionId, Timestamp = DateTime.Now,
                    Category = category.DisplayName, FilePath = path };
                var intentLogged = false;
                try
                {
                    var target = GetTargets(category.Id).FirstOrDefault(candidate => IsSafeCandidate(path, candidate));
                    if (target is null)
                    {
                        record.Status = OperationStatus.Skipped;
                        record.ErrorMessage = "Path is outside the allowed cache files, is missing, or is a reparse point.";
                    }
                    else if (OwnerIsRunning(target))
                    {
                        record.Status = OperationStatus.Skipped;
                        record.ErrorMessage = $"{target.ProcessName} is running. Close it and scan again.";
                    }
                    else
                    {
                        record.SizeBytes = new FileInfo(path).Length;
                        _history.BeginOperation(record);
                        intentLogged = true;
                        if (!IsSafeCandidate(path, target) || OwnerIsRunning(target))
                        {
                            record.Status = OperationStatus.Skipped;
                            record.ErrorMessage = "The file or application state changed before cleanup.";
                        }
                        else
                        {
                            File.Delete(path);
                            record.Status = OperationStatus.Deleted;
                        }
                    }
                }
                catch (Exception ex)
                {
                    record.Status = OperationStatus.Error;
                    record.ErrorMessage = ex.Message;
                }

                // If final persistence fails, the durable Pending row remains; abort rather than silently losing history.
                if (intentLogged) _history.CompleteOperation(record);
                else _history.LogOperation(record);
                session.Records.Add(record);
                if (record.Status == OperationStatus.Deleted) { session.FilesDeleted++; session.TotalBytesFreed += record.SizeBytes; }
                else if (record.Status == OperationStatus.Skipped) session.FilesSkipped++;
                else session.FilesErrored++;
            }
        });
        return session;
    }

    private static bool IsSafeCandidate(string path, CleanupTarget target)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(target.Directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
            var file = new FileInfo(fullPath);
            if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            if (target.ThumbnailOnly &&
                (!string.Equals(file.DirectoryName, fullRoot, StringComparison.OrdinalIgnoreCase) ||
                 !file.Name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) ||
                 !file.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase))) return false;
            for (var directory = file.Directory; directory is not null; directory = directory.Parent)
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                if (string.Equals(directory.FullName.TrimEnd(Path.DirectorySeparatorChar), fullRoot, StringComparison.OrdinalIgnoreCase))
                {
                    // The allowlisted root can itself sit beneath a replaced profile/junction.
                    for (var ancestor = directory.Parent; ancestor is not null; ancestor = ancestor.Parent)
                        if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return false;
    }

    private static string NormalizeCandidatePath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }
}

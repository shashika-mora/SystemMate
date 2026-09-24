using SystemMate.Models;

namespace SystemMate.Services;

/// <summary>
/// Scans well-known junk locations and deletes selected files.
/// Scan is fully read-only. Deletion validates every path immediately before touching it.
/// </summary>
public sealed class CleanerService
{
    private readonly HistoryService _history;

    public CleanerService(HistoryService history)
    {
        _history = history;
    }

    // ── Category definitions ─────────────────────────────────────────────────

    public IReadOnlyList<CleanupCategory> GetCategories() =>
    [
        new() { Id = "win_temp",      DisplayName = "Windows temporary files",  Description = "%TEMP% and %WINDIR%\\Temp",               IsSafe = true  },
        new() { Id = "browser_cache", DisplayName = "Browser caches",           Description = "Chrome, Edge, Firefox cache directories",  IsSafe = true  },
        new() { Id = "app_cache",     DisplayName = "Application caches",       Description = "VS Code, npm, pip, JetBrains caches",     IsSafe = true  },
        new() { Id = "recycle_bin",   DisplayName = "Recycle Bin",              Description = "Items currently in the Recycle Bin",      IsSafe = true  },
        new() { Id = "thumbnails",    DisplayName = "Thumbnail cache",          Description = "Windows thumbnail database files",        IsSafe = true  },
    ];

    // ── Scan ─────────────────────────────────────────────────────────────────

    public async Task<ScanResult> ScanAsync(IProgress<string>? progress = null)
    {
        var result = new ScanResult();
        var categories = GetCategories();
        result.Categories.AddRange(categories);

        await Task.Run(() =>
        {
            foreach (var cat in categories)
            {
                progress?.Report($"Scanning {cat.DisplayName}…");
                var paths = GetScanPaths(cat.Id);
                ScanPaths(paths, cat, progress);
            }
        });

        return result;
    }

    private static IEnumerable<string> GetScanPaths(string categoryId) => categoryId switch
    {
        "win_temp" =>
        [
            Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath(),
            Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? "C:\\Windows", "Temp"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
        ],

        "browser_cache" =>
        [
            // Chrome
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "User Data", "Default", "Cache"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "User Data", "Default", "Code Cache"),
            // Edge
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "User Data", "Default", "Cache"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "User Data", "Default", "Code Cache"),
            // Firefox
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Mozilla", "Firefox", "Profiles"),
        ],

        "app_cache" =>
        [
            // VS Code
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Code", "Cache"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Code", "CachedData"),
            // npm
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm-cache"),
            // pip
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "pip", "Cache"),
        ],

        "thumbnails" =>
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Explorer"),
        ],

        "recycle_bin" => [], // special-cased in ScanPaths

        _ => []
    };

    private static void ScanPaths(
        IEnumerable<string> paths,
        CleanupCategory category,
        IProgress<string>? progress = null)
    {
        if (category.Id == "recycle_bin")
        {
            ScanRecycleBin(category);
            return;
        }

        foreach (var dir in paths)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var file in EnumerateFilesSafely(dir, progress))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (!info.Exists) continue;
                        category.FilePaths.Add(file);
                        category.TotalSizeBytes += info.Length;
                        category.FileCount++;
                    }
                    catch { /* locked / access denied — skip */ }
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"Could not scan {dir}: {ex.Message}");
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesSafely(
        string root,
        IProgress<string>? progress)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            var filesToReturn = new List<string>();
            DirectoryInfo directoryInfo;
            try
            {
                directoryInfo = new DirectoryInfo(directory);
                if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                foreach (var file in directoryInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                {
                    if ((file.Attributes & FileAttributes.ReparsePoint) == 0)
                        filesToReturn.Add(file.FullName);
                }

                foreach (var child in directoryInfo.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
                {
                    if ((child.Attributes & FileAttributes.ReparsePoint) == 0)
                        pending.Push(child.FullName);
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"Could not scan {directory}: {ex.Message}");
            }

            foreach (var file in filesToReturn)
                yield return file;
        }
    }

    private static void ScanRecycleBin(CleanupCategory category)
    {
        // Recycle Bin sits at $Recycle.Bin on each drive
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            var recyclePath = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
            if (!Directory.Exists(recyclePath)) continue;
            try
            {
                foreach (var file in EnumerateFilesSafely(recyclePath, null))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (!info.Exists) continue;
                        // Skip index files ($I...) — only count actual deleted files ($R...)
                        if (Path.GetFileName(file).StartsWith("$I", StringComparison.OrdinalIgnoreCase))
                            continue;
                        category.FilePaths.Add(file);
                        category.TotalSizeBytes += info.Length;
                        category.FileCount++;
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    // ── Clean ────────────────────────────────────────────────────────────────

    public async Task<CleanupSession> CleanAsync(
        ScanResult scanResult,
        IProgress<(string file, int done, int total)>? progress = null)
    {
        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var session = new CleanupSession
        {
            SessionId  = sessionId,
            StartedAt  = DateTime.Now,
        };

        var selectedCategories = scanResult.Categories.Where(c => c.IsSelected).ToList();
        var allFiles = selectedCategories.SelectMany(c =>
            c.FilePaths.Select(f => (File: f, Category: c.DisplayName, CategoryId: c.Id))).ToList();
        int total = allFiles.Count, done = 0;

        await Task.Run(() =>
        {
            foreach (var (filePath, categoryName, categoryId) in allFiles)
            {
                done++;
                progress?.Report((filePath, done, total));

                var record = new OperationRecord
                {
                    SessionId  = sessionId,
                    Timestamp  = DateTime.Now,
                    Category   = categoryName,
                    FilePath   = filePath,
                };

                try
                {
                    var roots = GetScanPaths(categoryId);
                    if (categoryId == "recycle_bin")
                        roots = DriveInfo.GetDrives().Where(d => d.IsReady)
                            .Select(d => Path.Combine(d.RootDirectory.FullName, "$Recycle.Bin"));
                    if (!IsSafeCandidate(filePath, roots))
                    {
                        record.Status = OperationStatus.Skipped;
                        record.ErrorMessage = "Path is outside the cleanup root or is a reparse point.";
                        session.FilesSkipped++;
                    }
                    else
                    {
                        var info = new FileInfo(filePath);
                        if (!info.Exists)
                        {
                            record.Status = OperationStatus.Skipped;
                            session.FilesSkipped++;
                        }
                        else
                        {
                            record.SizeBytes = info.Length;
                            if (!IsSafeCandidate(filePath, roots))
                            {
                                record.Status = OperationStatus.Skipped;
                                record.ErrorMessage = "Path changed before deletion and is no longer safe.";
                                session.FilesSkipped++;
                            }
                            else
                            {
                                File.Delete(filePath);
                                record.Status = OperationStatus.Deleted;
                                session.TotalBytesFreed += record.SizeBytes;
                                session.FilesDeleted++;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    record.Status = OperationStatus.Error;
                    record.ErrorMessage = ex.Message;
                    session.FilesErrored++;
                }

                _history.LogOperation(record);
                session.Records.Add(record);
            }
        });

        return session;
    }

    private static bool IsSafeCandidate(string filePath, IEnumerable<string> roots)
    {
        var fullPath = Path.GetFullPath(filePath);
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists || (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            return false;

        foreach (var root in roots)
        {
            var fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!IsDescendant(fullPath, fullRoot))
                continue;

            var current = fileInfo.Directory;
            while (current is not null &&
                   fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase))
            {
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                    return false;
                if (string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar),
                    fullRoot, StringComparison.OrdinalIgnoreCase))
                    return true;
                current = current.Parent;
            }
        }

        return false;
    }

    private static bool IsDescendant(string path, string root) =>
        path.StartsWith(root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}

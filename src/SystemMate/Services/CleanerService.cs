using SystemMate.Models;

namespace SystemMate.Services;

/// <summary>
/// Scans well-known junk locations and deletes selected files.
/// Scan is fully read-only. Deletion logs to HistoryService before touching any file.
/// </summary>
public sealed class CleanerService
{
    private readonly HistoryService _history;

    public CleanerService() : this(App.History) { }

    internal CleanerService(HistoryService history)
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
                ScanPaths(paths, cat);
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

    private static void ScanPaths(IEnumerable<string> paths, CleanupCategory category)
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
                var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories);
                foreach (var file in files)
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
            catch { /* directory inaccessible — skip */ }
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
                foreach (var file in Directory.EnumerateFiles(recyclePath, "*",
                    SearchOption.AllDirectories))
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
            c.FilePaths.Select(f => (File: f, Category: c.DisplayName))).ToList();
        int total = allFiles.Count, done = 0;

        await Task.Run(() =>
        {
            foreach (var (filePath, categoryName) in allFiles)
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
                    var info = new FileInfo(filePath);
                    record.SizeBytes = info.Exists ? info.Length : 0;

                    // Log BEFORE deletion — guarantees history even on crash
                    record.Status = OperationStatus.Deleted;
                    _history.LogOperation(record);

                    if (info.Exists)
                    {
                        File.Delete(filePath);
                        session.TotalBytesFreed += record.SizeBytes;
                        session.FilesDeleted++;
                    }
                    else
                    {
                        record.Status = OperationStatus.Skipped;
                        session.FilesSkipped++;
                    }
                }
                catch (Exception ex)
                {
                    record.Status = OperationStatus.Error;
                    record.ErrorMessage = ex.Message;
                    _history.LogOperation(record);
                    session.FilesErrored++;
                }

                session.Records.Add(record);
            }
        });

        return session;
    }
}

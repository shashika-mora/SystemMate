using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Security;
using System.Text.Json;
using SystemMate.Models;

namespace SystemMate.Services;

[SupportedOSPlatform("windows")]
public sealed class StartupService
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOncePath = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    private readonly string _backupDirectory;
    private readonly string _runKeyPath;
    private readonly SemaphoreSlim _changeGate = new(1, 1);

    private sealed record StartupBackup(string ValueName, string Command, int View, int ValueKind, DateTime CreatedUtc);

    public StartupService() : this(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemMate", "StartupBackups"),
        RunPath)
    {
    }

    // Tests use an isolated HKCU subtree; the app always uses the parameterless constructor.
    internal StartupService(string backupDirectory, string runKeyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(runKeyPath);
        _backupDirectory = Path.GetFullPath(backupDirectory);
        _runKeyPath = runKeyPath;
    }

    public Task<StartupInventoryResult> ScanAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var items = new List<StartupItem>();
        var warnings = new HashSet<string>();
        var views = Environment.Is64BitOperatingSystem
            ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
            : new[] { RegistryView.Registry32 };
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in views)
        foreach (var path in new[] { RunPath, RunOncePath })
        {
            cancellationToken.ThrowIfCancellationRequested();
            // HKCU Run/RunOnce are shared across WOW64 views. Reading both duplicates
            // the same entries and can pair them with the wrong approval metadata.
            if (hive == RegistryHive.CurrentUser && Environment.Is64BitOperatingSystem && view == RegistryView.Registry32) continue;
            var source = $"{(hive == RegistryHive.CurrentUser ? "Current user" : "All users")} · {(path == RunPath ? "Run" : "RunOnce")} · {(view == RegistryView.Registry64 ? "64-bit" : "32-bit")}";
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(path);
                if (key is null) continue;
                foreach (var name in key.GetValueNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var command = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                        if (string.IsNullOrWhiteSpace(command)) continue;
                        var kind = key.GetValueKind(name);
                        var status = path == RunOncePath ? "Runs once at sign-in" : ReadApprovedStatus(root,
                            hive == RegistryHive.LocalMachine && Environment.Is64BitOperatingSystem && view == RegistryView.Registry32 ? "Run32" : "Run", name);
                        items.Add(new StartupItem
                        {
                            Name = string.IsNullOrEmpty(name) ? "(Default)" : name, Command = command, Source = source,
                            Status = status, RegistryValueName = name, RegistryView = (int)view, RegistryValueKind = (int)kind,
                            CanDisable = hive == RegistryHive.CurrentUser && path == RunPath && status != "Disabled in Windows" && kind is RegistryValueKind.String or RegistryValueKind.ExpandString
                        });
                    }
                    catch (Exception ex) when (IsInventoryError(ex)) { warnings.Add($"Some {source.ToLowerInvariant()} entries could not be read."); }
                }
            }
            catch (Exception ex) when (IsInventoryError(ex)) { warnings.Add($"Could not read {source.ToLowerInvariant()} entries."); }
        }
        ReadStartupFolder(Environment.SpecialFolder.Startup, "Current user · Startup folder", RegistryHive.CurrentUser, items, warnings, cancellationToken);
        ReadStartupFolder(Environment.SpecialFolder.CommonStartup, "All users · Startup folder", RegistryHive.LocalMachine, items, warnings, cancellationToken);
        ReadDisabledBackups(items, warnings, cancellationToken);
        return new StartupInventoryResult(items.DistinctBy(x => (x.Name, x.Command, x.Source))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), warnings.ToArray());
    }, cancellationToken);

    public async Task DisableAsync(StartupItem item, CancellationToken cancellationToken = default)
    {
        if (!item.CanDisable || item.CanRestore) throw new InvalidOperationException("Only current-user Run entries can be disabled here.");
        ValidateRegistryIdentity(item.RegistryView, item.RegistryValueKind);
        await _changeGate.WaitAsync(cancellationToken);
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, (RegistryView)item.RegistryView);
            using var key = root.OpenSubKey(_runKeyPath, writable: true) ?? throw new InvalidOperationException("The startup entry no longer exists. Refresh the list.");
            EnsureUnchanged(key, item);
            Directory.CreateDirectory(_backupDirectory);
            var backupPath = Path.Combine(_backupDirectory, $"{DateTime.UtcNow:yyyyMMddTHHmmssfff}_{Guid.NewGuid():N}.json");
            var backup = new StartupBackup(item.RegistryValueName, item.Command, item.RegistryView, item.RegistryValueKind, DateTime.UtcNow);
            using (var stream = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, backup);
                stream.Flush(flushToDisk: true);
            }
            if (ReadBackup(backupPath) != backup) throw new IOException("The startup backup could not be verified; no entry was changed.");
            cancellationToken.ThrowIfCancellationRequested();
            EnsureUnchanged(key, item);
            key.DeleteValue(item.RegistryValueName, throwOnMissingValue: true);
        }
        finally { _changeGate.Release(); }
    }

    public async Task RestoreAsync(StartupItem item, CancellationToken cancellationToken = default)
    {
        if (!item.CanRestore) throw new InvalidOperationException("Select an entry disabled by SystemMate.");
        var fullPath = Path.GetFullPath(item.BackupPath);
        if (!string.Equals(Path.GetDirectoryName(fullPath), Path.GetFullPath(_backupDirectory), StringComparison.OrdinalIgnoreCase)
            || !fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The startup backup path is invalid.");
        await _changeGate.WaitAsync(cancellationToken);
        try
        {
            var backup = ReadBackup(fullPath);
            ValidateRegistryIdentity(backup.View, backup.ValueKind);
            if (backup.ValueName != item.RegistryValueName || backup.Command != item.Command || backup.View != item.RegistryView || backup.ValueKind != item.RegistryValueKind)
                throw new InvalidOperationException("The startup backup changed. Refresh and review the entry again.");
            if (File.Exists(fullPath + ".restored")) throw new InvalidOperationException("This backup has already been restored. Refresh the list.");
            cancellationToken.ThrowIfCancellationRequested();
            using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, (RegistryView)backup.View);
            using var key = root.CreateSubKey(_runKeyPath, writable: true);
            if (key.GetValueNames().Contains(backup.ValueName, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("An entry with this name already exists. It was not overwritten.");
            key.SetValue(backup.ValueName, backup.Command, (RegistryValueKind)backup.ValueKind);
            EnsureUnchanged(key, item);
            // Keep the original backup after restoration; the marker prevents stale entries resurfacing later.
            try { File.WriteAllText(fullPath + ".restored", DateTime.UtcNow.ToString("O")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException("The entry was restored, but its completion record could not be saved. Refresh the list before another change.", ex);
            }
        }
        finally { _changeGate.Release(); }
    }

    private static void EnsureUnchanged(RegistryKey key, StartupItem item)
    {
        if (!key.GetValueNames().Contains(item.RegistryValueName, StringComparer.OrdinalIgnoreCase)
            || key.GetValue(item.RegistryValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string != item.Command
            || (int)key.GetValueKind(item.RegistryValueName) != item.RegistryValueKind)
            throw new InvalidOperationException("The startup entry changed. Refresh and review it again.");
    }

    private static void ValidateRegistryIdentity(int view, int kind)
    {
        if (view != (int)RegistryView.Registry64 && view != (int)RegistryView.Registry32)
            throw new InvalidOperationException("Unsupported registry view.");
        if (kind != (int)RegistryValueKind.String && kind != (int)RegistryValueKind.ExpandString)
            throw new InvalidOperationException("Unsupported startup value type.");
    }

    private static StartupBackup ReadBackup(string path)
    {
        var backup = JsonSerializer.Deserialize<StartupBackup>(File.ReadAllText(path));
        if (backup is null || backup.ValueName is null || string.IsNullOrWhiteSpace(backup.Command))
            throw new InvalidDataException("The startup backup is incomplete.");
        return backup;
    }

    private void ReadDisabledBackups(List<StartupItem> items, HashSet<string> warnings, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_backupDirectory)) return;
        try
        {
            var seen = new HashSet<(int View, string Name)>();
            foreach (var path in Directory.EnumerateFiles(_backupDirectory, "*.json").OrderByDescending(x => x, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var backup = ReadBackup(path);
                    ValidateRegistryIdentity(backup.View, backup.ValueKind);
                    if (!seen.Add((backup.View, backup.ValueName.ToUpperInvariant())) || File.Exists(path + ".restored")) continue;
                    using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, (RegistryView)backup.View);
                    using var key = root.OpenSubKey(_runKeyPath);
                    if (key?.GetValueNames().Contains(backup.ValueName, StringComparer.OrdinalIgnoreCase) == true) continue;
                    items.Add(new StartupItem
                    {
                        Name = string.IsNullOrEmpty(backup.ValueName) ? "(Default)" : backup.ValueName,
                        RegistryValueName = backup.ValueName, Command = backup.Command, Source = "Current user · SystemMate backup",
                        Status = "Disabled by SystemMate", RegistryView = backup.View, RegistryValueKind = backup.ValueKind, BackupPath = path
                    });
                }
                catch (Exception ex) when (IsInventoryError(ex) || ex is JsonException or InvalidOperationException) { warnings.Add("Some saved startup backups could not be read."); }
            }
        }
        catch (Exception ex) when (IsInventoryError(ex)) { warnings.Add("Saved startup backups could not be read."); }
    }

    private static void ReadStartupFolder(Environment.SpecialFolder folder, string source, RegistryHive hive,
        List<StartupItem> items, HashSet<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            var path = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            using var root = RegistryKey.OpenBaseKey(hive, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
            foreach (var file in Directory.EnumerateFiles(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(Path.GetFileName(file), "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new StartupItem { Name = Path.GetFileNameWithoutExtension(file), Command = file, Source = source, Status = ReadApprovedStatus(root, "StartupFolder", Path.GetFileName(file)) });
            }
        }
        catch (Exception ex) when (IsInventoryError(ex)) { warnings.Add($"Could not read the {source.ToLowerInvariant()}."); }
    }

    private static string ReadApprovedStatus(RegistryKey root, string group, string name)
    {
        try
        {
            using var approved = root.OpenSubKey($@"{ApprovedPath}\{group}");
            if (approved?.GetValue(name) is byte[] { Length: > 0 } data)
                return data[0] switch { 2 or 6 => "Enabled in Windows", 3 or 7 => "Disabled in Windows", _ => "Registered · status unknown" };
        }
        catch (Exception ex) when (IsInventoryError(ex)) { }
        return "Registered · status unknown";
    }

    private static bool IsInventoryError(Exception ex) => ex is UnauthorizedAccessException or SecurityException
        or IOException or InvalidDataException or ArgumentException;
}

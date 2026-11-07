using Microsoft.Win32;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security;
using SystemMate.Models;

namespace SystemMate.Services;

[SupportedOSPlatform("windows")]
public sealed class AppInventoryService
{
    private const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public Task<AppInventoryResult> ScanAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var items = new List<AppInventoryItem>();
        var warnings = new HashSet<string>();
        var views = Environment.Is64BitOperatingSystem
            ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
            : new[] { RegistryView.Registry32 };
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in views)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = $"{(hive == RegistryHive.LocalMachine ? "All users" : "Current user")} / {(view == RegistryView.Registry64 ? "64-bit" : "32-bit")}";
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(UninstallPath);
                if (uninstall is null) continue;
                foreach (var subkeyName in uninstall.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var entry = uninstall.OpenSubKey(subkeyName);
                        if (entry is null || Convert.ToInt32(entry.GetValue("SystemComponent", 0), CultureInfo.InvariantCulture) == 1) continue;
                        var name = entry.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var date = entry.GetValue("InstallDate") as string ?? "";
                        if (DateTime.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                            date = parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        long? size = null;
                        if (entry.GetValue("EstimatedSize") is int kb && kb > 0) size = (long)kb * 1024;
                        items.Add(new AppInventoryItem
                        {
                            Name = name.Trim(), Version = entry.GetValue("DisplayVersion") as string ?? "",
                            Publisher = entry.GetValue("Publisher") as string ?? "", ReportedDate = date,
                            EstimatedSizeBytes = size, Location = entry.GetValue("InstallLocation") as string ?? "",
                            Scope = source, RegistryPath = $"{root.Name}\\{UninstallPath}\\{subkeyName}"
                        });
                    }
                    catch (Exception ex) when (IsInventoryError(ex)) { warnings.Add($"Some {source.ToLowerInvariant()} entries could not be read."); }
                }
            }
            catch (Exception ex) when (IsInventoryError(ex)) { warnings.Add($"Could not read {source.ToLowerInvariant()} applications."); }
        }
        // HKCU uninstall keys can be shared between registry views. Keep separately named versions/installations.
        var result = items.DistinctBy(x => (x.RegistryPath.ToUpperInvariant(), x.Name, x.Version, x.Location))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        return new AppInventoryResult(result, warnings.ToArray());
    }, cancellationToken);

    private static bool IsInventoryError(Exception ex) => ex is UnauthorizedAccessException or SecurityException
        or IOException or InvalidCastException or FormatException or OverflowException;
}

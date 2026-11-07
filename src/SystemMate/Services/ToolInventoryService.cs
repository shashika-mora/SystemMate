using System.Management;
using SystemMate.Models;

namespace SystemMate.Services;

public sealed class ToolInventoryService
{
    public Task<InventoryResult> GetAsync(string kind, CancellationToken token = default) => Task.Run(() =>
    {
        var (scope, query) = kind switch
        {
            "drivers" => ("root\\cimv2", "SELECT DeviceName, DriverVersion, DriverProviderName, DriverDate, IsSigned FROM Win32_PnPSignedDriver"),
            "services" => ("root\\cimv2", "SELECT Name, DisplayName, State, StartMode, Description FROM Win32_Service"),
            "restore" => ("root\\default", "SELECT Description, CreationTime, SequenceNumber FROM SystemRestore"),
            _ => throw new ArgumentException("Unknown inventory.", nameof(kind))
        };
        var rows = new List<InventoryEntry>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query)
            {
                Options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(20), ReturnImmediately = false }
            };
            using var values = searcher.Get();
            foreach (ManagementObject value in values)
            {
                using (value)
                {
                    token.ThrowIfCancellationRequested();
                    string Text(string key) => value[key]?.ToString() ?? "Unavailable";
                    rows.Add(kind switch
                    {
                        "drivers" => new(Text("DeviceName"),
                            $"{Text("DriverProviderName")} · {Text("DriverVersion")} · {FormatDate(Text("DriverDate"))}",
                            value["IsSigned"] is bool signed ? signed ? "Signed" : "Not signed" : "Signature unavailable"),
                        "services" => new(Text("DisplayName"), $"{Text("Name")} · Startup: {Text("StartMode")}\n{Text("Description")}", Text("State")),
                        _ => new(Text("Description"), $"Restore point #{Text("SequenceNumber")}", FormatDate(Text("CreationTime")))
                    });
                }
            }
            return new InventoryResult(rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return new InventoryResult(rows, $"Inventory is unavailable or incomplete: {ex.Message}");
        }
    }, token);

    private static string FormatDate(string value)
    {
        try { return ManagementDateTimeConverter.ToDateTime(value).ToString("g"); }
        catch (ArgumentException) { return "Date unavailable"; }
    }
}

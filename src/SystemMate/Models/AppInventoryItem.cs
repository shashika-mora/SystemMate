namespace SystemMate.Models;

public sealed record AppInventoryItem
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string ReportedDate { get; set; } = "";
    public long? EstimatedSizeBytes { get; set; }
    public string Location { get; set; } = "";
    public string Scope { get; set; } = "";
    public string RegistryPath { get; set; } = "";
    public string SizeDisplay => EstimatedSizeBytes is long bytes ? FormatBytes(bytes) : "Not reported";

    private static string FormatBytes(long bytes) => bytes >= 1_073_741_824
        ? $"{bytes / 1_073_741_824d:F1} GB"
        : $"{bytes / 1_048_576d:F1} MB";
}

public sealed record AppInventoryResult(IReadOnlyList<AppInventoryItem> Items, IReadOnlyList<string> Warnings);

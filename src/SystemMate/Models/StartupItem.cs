namespace SystemMate.Models;

public sealed record StartupItem
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Source { get; set; } = "";
    public string Status { get; set; } = "Unknown";
    public string RegistryValueName { get; set; } = "";
    public int RegistryView { get; set; }
    public int RegistryValueKind { get; set; }
    public bool CanDisable { get; set; }
    public string BackupPath { get; set; } = "";
    public bool CanRestore => !string.IsNullOrEmpty(BackupPath);
}

public sealed record StartupInventoryResult(IReadOnlyList<StartupItem> Items, IReadOnlyList<string> Warnings);

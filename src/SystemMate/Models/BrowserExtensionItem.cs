namespace SystemMate.Models;

public sealed record BrowserExtensionItem
{
    public string Browser { get; set; } = "";
    public string Profile { get; set; } = "";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "";
    public string MetadataPath { get; set; } = "";
    public string ManagerAddress { get; set; } = "";
    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> OptionalPermissions { get; set; } = Array.Empty<string>();
    public string PermissionSummary => $"{Permissions.Count} listed / {OptionalPermissions.Count} optional";
}

public sealed record BrowserExtensionRoot(string Browser, string RootPath, bool IsFirefox = false);
public sealed record BrowserExtensionInventoryResult(IReadOnlyList<BrowserExtensionItem> Items, IReadOnlyList<string> Warnings);

namespace SystemMate.Models;

public sealed record SystemTool(string Id, string Title, string Category, string Description,
    string Executable, string[] Arguments, bool RequiresAdministrator = false, bool ChangesSystem = false)
{
    public string ActionLabel => RequiresAdministrator ? "Open administrator tool" : "Run";
    public string CommandPreview => $"{Executable} {string.Join(' ', Arguments)}";
    public string AccessDescription => RequiresAdministrator ? "Windows will request administrator approval." : "Runs as your current user.";
}

public sealed record CommandResult(int? ExitCode, string Output, bool OpenedExternalWindow = false);

public sealed record InventoryEntry(string Name, string Detail, string State);

public sealed record InventoryResult(IReadOnlyList<InventoryEntry> Entries, string? Warning = null);

public sealed record PowerPlan(Guid Id, string Name, bool IsActive)
{
    public string DisplayName => IsActive ? $"{Name} · Active" : Name;
}

public sealed record ProcessUsage(int Id, string Name, double CpuPercent, long WorkingSetBytes, int ThreadCount)
{
    public string CpuDisplay => $"{CpuPercent:F1}%";
    public string MemoryDisplay => $"{WorkingSetBytes / 1048576d:N0} MB";
}

public sealed record PerformanceSample(IReadOnlyList<ProcessUsage> Processes, string ProcessorName,
    int LogicalProcessors, double? FrequencyMhz, double? TotalCpuPercent);

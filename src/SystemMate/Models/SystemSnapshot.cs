namespace SystemMate.Models;

/// <summary>Point-in-time snapshot of key system metrics.</summary>
public sealed class SystemSnapshot
{
    public double CpuUsagePercent { get; init; }
    public ulong RamUsedBytes { get; init; }
    public ulong RamTotalBytes { get; init; }
    public long DiskUsedBytes { get; init; }
    public long DiskTotalBytes { get; init; }
    public DateTime LastBootTime { get; init; }
    public string OsVersion { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
    public string CpuName { get; init; } = string.Empty;
    public string? DiskHealthStatus { get; init; }

    public double RamUsagePercent =>
        RamTotalBytes > 0 ? (double)RamUsedBytes / RamTotalBytes * 100 : 0;

    public double DiskUsagePercent =>
        DiskTotalBytes > 0 ? (double)DiskUsedBytes / DiskTotalBytes * 100 : 0;

    public TimeSpan Uptime => DateTime.Now - LastBootTime;

    public string BootTimeDisplay =>
        (int)Uptime.TotalSeconds < 120
            ? $"{(int)Uptime.TotalSeconds} sec"
            : (int)Uptime.TotalMinutes < 120
                ? $"{(int)Uptime.TotalMinutes} min"
                : $"{(int)Uptime.TotalHours} hr {Uptime.Minutes} min";
}

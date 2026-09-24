namespace SystemMate.Models;

public enum OperationStatus { Deleted, Skipped, Error }

/// <summary>Single file operation record — written to SQLite before any deletion.</summary>
public sealed class OperationRecord
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public OperationStatus Status { get; set; }
    public string? ErrorMessage { get; set; }

    public string SizeDisplay => SizeBytes switch
    {
        >= 1_048_576 => $"{SizeBytes / 1_048_576.0:F1} MB",
        >= 1_024     => $"{SizeBytes / 1_024.0:F0} KB",
        _            => $"{SizeBytes} B"
    };
}

/// <summary>Aggregated view of one cleanup session.</summary>
public sealed class CleanupSession
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public long TotalBytesFreed { get; set; }
    public int FilesDeleted { get; set; }
    public int FilesSkipped { get; set; }
    public int FilesErrored { get; set; }
    public bool ShowFileDetails { get; set; } = true;
    public List<OperationRecord> Records { get; set; } = new();

    public string TotalSizeDisplay => TotalBytesFreed switch
    {
        >= 1_073_741_824 => $"{TotalBytesFreed / 1_073_741_824.0:F2} GB",
        >= 1_048_576     => $"{TotalBytesFreed / 1_048_576.0:F1} MB",
        >= 1_024         => $"{TotalBytesFreed / 1_024.0:F0} KB",
        _                => $"{TotalBytesFreed} B"
    };
}

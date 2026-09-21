namespace SystemMate.Models;

/// <summary>A logical group of junk files in one cleanup category.</summary>
public sealed class CleanupCategory
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public long TotalSizeBytes { get; set; }
    public int FileCount { get; set; }
    public bool IsSelected { get; set; } = true;
    public bool IsSafe { get; init; } = true;

    /// <summary>Files found in this category (populated after scan).</summary>
    public List<string> FilePaths { get; } = new();

    public string SizeDisplay => BytesToDisplay(TotalSizeBytes);

    private static string BytesToDisplay(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F0} MB",
        >= 1_024         => $"{bytes / 1_024.0:F0} KB",
        _                => $"{bytes} B"
    };
}

/// <summary>Result of a full scan across all categories.</summary>
public sealed class ScanResult
{
    public DateTime ScannedAt { get; } = DateTime.Now;
    public List<CleanupCategory> Categories { get; } = new();
    public long TotalReclaimableBytes => Categories.Where(c => c.IsSelected).Sum(c => c.TotalSizeBytes);
    public int TotalFileCount => Categories.Where(c => c.IsSelected).Sum(c => c.FileCount);

    public string TotalSizeDisplay
    {
        get
        {
            var bytes = TotalReclaimableBytes;
            return bytes switch
            {
                >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
                >= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
                >= 1_024         => $"{bytes / 1_024.0:F0} KB",
                _                => $"{bytes} B"
            };
        }
    }
}

namespace SystemMate.Models;

public sealed class StorageAnalysisOptions
{
    public bool FindDuplicates { get; init; } = true;
    public int MaximumFiles { get; init; } = 100_000;
    public int MaximumEntries { get; init; } = 200_000;
    public int MaximumDepth { get; init; } = 128;
    public int MaximumDuplicateGroups { get; init; } = 500;
}

public sealed record StorageFile(string FullPath, long SizeBytes, DateTime LastWriteTimeUtc)
{
    public string Name => Path.GetFileName(FullPath);
    public string SizeDisplay => StorageAnalysisResult.FormatBytes(SizeBytes);
}

public sealed record StorageBreakdown(string Name, long SizeBytes, int FileCount)
{
    public string SizeDisplay => StorageAnalysisResult.FormatBytes(SizeBytes);
    public string Summary => $"{SizeDisplay} · {FileCount:N0} files";
}

public sealed record StorageDuplicateGroup(string Sha256, long FileSizeBytes, IReadOnlyList<string> FilePaths)
{
    public long DuplicateCopyBytes => FileSizeBytes * (FilePaths.Count - 1);
    public string Summary => $"{FilePaths.Count:N0} matching files · {StorageAnalysisResult.FormatBytes(FileSizeBytes)} each";
    public string PathsDisplay => string.Join(Environment.NewLine, FilePaths.Take(25))
        + (FilePaths.Count > 25 ? $"{Environment.NewLine}… and {FilePaths.Count - 25:N0} more paths (included in CSV export)" : string.Empty);
}

public sealed record StorageAnalysisProgress(string Stage, int FileCount, long TotalBytes, string CurrentPath);

public sealed class StorageAnalysisResult
{
    public required string RootPath { get; init; }
    public int FileCount { get; init; }
    public long TotalBytes { get; init; }
    public int SkippedEntries { get; init; }
    public bool IsPartial { get; init; }
    public bool DuplicateGroupsLimited { get; init; }
    public bool DuplicatesChecked { get; init; }
    public required IReadOnlyList<StorageFile> LargestFiles { get; init; }
    public required IReadOnlyList<StorageBreakdown> FileTypes { get; init; }
    public required IReadOnlyList<StorageBreakdown> Folders { get; init; }
    public required IReadOnlyList<StorageDuplicateGroup> DuplicateGroups { get; init; }
    public DateTime AnalyzedAt { get; init; } = DateTime.Now;
    public string TotalDisplay => FormatBytes(TotalBytes);
    public long DuplicateCopyBytes => DuplicateGroups.Sum(group => group.DuplicateCopyBytes);
    public string DuplicateDisplay => FormatBytes(DuplicateCopyBytes);

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_099_511_627_776 => $"{bytes / 1_099_511_627_776.0:F2} TB",
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024 => $"{bytes / 1_024.0:F1} KB",
        _ => $"{bytes:N0} B"
    };
}

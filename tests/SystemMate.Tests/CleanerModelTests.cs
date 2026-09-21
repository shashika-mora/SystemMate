using SystemMate.Models;
using Xunit;

namespace SystemMate.Tests;

/// <summary>Tests for ScanResult and CleanupCategory logic (no file system access).</summary>
public sealed class CleanerModelTests
{
    [Fact]
    public void ScanResult_TotalReclaimableBytes_SumsSelectedOnly()
    {
        var result = new ScanResult();
        result.Categories.Add(new CleanupCategory
        {
            Id = "a", DisplayName = "A", Description = "A",
            TotalSizeBytes = 1_000_000, IsSelected = true
        });
        result.Categories.Add(new CleanupCategory
        {
            Id = "b", DisplayName = "B", Description = "B",
            TotalSizeBytes = 2_000_000, IsSelected = false
        });

        Assert.Equal(1_000_000, result.TotalReclaimableBytes);
    }

    [Fact]
    public void ScanResult_TotalFileCount_SumsSelectedOnly()
    {
        var result = new ScanResult();
        result.Categories.Add(new CleanupCategory
        {
            Id = "a", DisplayName = "A", Description = "A",
            FileCount = 10, IsSelected = true
        });
        result.Categories.Add(new CleanupCategory
        {
            Id = "b", DisplayName = "B", Description = "B",
            FileCount = 5, IsSelected = false
        });

        Assert.Equal(10, result.TotalFileCount);
    }

    [Theory]
    [InlineData(500, "500 B")]
    [InlineData(1_500, "1 KB")]
    [InlineData(1_500_000, "1 MB")]
    [InlineData(1_500_000_000, "1.40 GB")]
    public void ScanResult_TotalSizeDisplay_FormatsCorrectly(long bytes, string expected)
    {
        var result = new ScanResult();
        result.Categories.Add(new CleanupCategory
        {
            Id = "x", DisplayName = "X", Description = "X",
            TotalSizeBytes = bytes, IsSelected = true
        });

        Assert.Equal(expected, result.TotalSizeDisplay);
    }

    [Fact]
    public void CleanupCategory_SizeDisplay_FormatsBytes()
    {
        var cat = new CleanupCategory
        {
            Id = "t", DisplayName = "T", Description = "T",
            TotalSizeBytes = 2_097_152  // exactly 2 MB
        };

        Assert.Equal("2 MB", cat.SizeDisplay);
    }

    [Fact]
    public void SystemSnapshot_Uptime_CalculatedFromBootTime()
    {
        var boot = DateTime.Now.AddHours(-3);
        var snap = new SystemSnapshot { LastBootTime = boot };

        Assert.True(snap.Uptime.TotalHours >= 3);
    }

    [Fact]
    public void SystemSnapshot_RamUsagePercent_ZeroWhenTotalZero()
    {
        var snap = new SystemSnapshot { RamUsedBytes = 0, RamTotalBytes = 0 };
        Assert.Equal(0, snap.RamUsagePercent);
    }

    [Fact]
    public void SystemSnapshot_DiskUsagePercent_CorrectRatio()
    {
        var snap = new SystemSnapshot
        {
            DiskUsedBytes = 250_000_000_000L,
            DiskTotalBytes = 500_000_000_000L
        };
        Assert.Equal(50.0, snap.DiskUsagePercent);
    }
}

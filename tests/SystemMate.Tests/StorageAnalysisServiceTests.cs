using SystemMate.Models;
using SystemMate.Services;
using System.Diagnostics;
using Xunit;

namespace SystemMate.Tests;

public sealed class StorageAnalysisServiceTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), $"systemmate_storage_{Guid.NewGuid():N}");

    public StorageAnalysisServiceTests() => Directory.CreateDirectory(_testRoot);

    [Fact]
    public async Task AnalyzeAsync_MatchesContentsRatherThanNamesOrSize()
    {
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "first.txt"), "same content");
        Directory.CreateDirectory(Path.Combine(_testRoot, "nested"));
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "nested", "renamed.bin"), "same content");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "different.txt"), "diff content");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "empty-a"), "");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "empty-b"), "");

        var result = await new StorageAnalysisService().AnalyzeAsync(_testRoot);

        Assert.Equal(5, result.FileCount);
        Assert.Equal(36, result.TotalBytes);
        var group = Assert.Single(result.DuplicateGroups);
        Assert.Equal(2, group.FilePaths.Count);
        Assert.Equal(12, group.DuplicateCopyBytes);
        Assert.Equal(64, group.Sha256.Length);
        Assert.DoesNotContain(group.FilePaths, path => Path.GetFileName(path) == "different.txt");
        Assert.False(result.IsPartial);
        Assert.All(result.LargestFiles, file => Assert.True(File.Exists(file.FullPath)));
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotIncludeSiblingFolders()
    {
        var selected = Directory.CreateDirectory(Path.Combine(_testRoot, "selected")).FullName;
        var sibling = Directory.CreateDirectory(Path.Combine(_testRoot, "outside")).FullName;
        await File.WriteAllTextAsync(Path.Combine(selected, "included.txt"), "inside");
        await File.WriteAllTextAsync(Path.Combine(sibling, "excluded.txt"), "outside");

        var result = await new StorageAnalysisService().AnalyzeAsync(selected);

        Assert.Equal(1, result.FileCount);
        Assert.Equal(6, result.TotalBytes);
        Assert.Equal("included.txt", Assert.Single(result.LargestFiles).Name);
        Assert.Empty(result.DuplicateGroups);
    }

    [Fact]
    public async Task AnalyzeAsync_ReportsPartialWhenFileLimitIsReached()
    {
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "first.txt"), "one");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "second.txt"), "two");

        var result = await new StorageAnalysisService().AnalyzeAsync(_testRoot,
            new StorageAnalysisOptions { MaximumFiles = 1 });

        Assert.True(result.IsPartial);
        Assert.Equal(1, result.FileCount);
        Assert.Equal(2, Directory.GetFiles(_testRoot).Length);
    }

    [Fact]
    public async Task AnalyzeAsync_CanMeasureWithoutReadingDuplicateContents()
    {
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "first.txt"), "same");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "second.txt"), "same");

        var result = await new StorageAnalysisService().AnalyzeAsync(_testRoot,
            new StorageAnalysisOptions { FindDuplicates = false });

        Assert.Equal(2, result.FileCount);
        Assert.False(result.DuplicatesChecked);
        Assert.Empty(result.DuplicateGroups);
    }

    [Fact]
    public async Task AnalyzeAsync_HonorsCancellationBeforeTraversal()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new StorageAnalysisService().AnalyzeAsync(_testRoot, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task AnalyzeAsync_ReportsMissingRootInsteadOfAnEmptySuccess()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            new StorageAnalysisService().AnalyzeAsync(Path.Combine(_testRoot, "missing")));
    }

    [Fact]
    public async Task AnalyzeAsync_ExcludesFilesChangedAfterMeasurementFromDuplicateMatches()
    {
        var changingFile = Path.Combine(_testRoot, "changing.txt");
        await File.WriteAllTextAsync(changingFile, "same");
        await File.WriteAllTextAsync(Path.Combine(_testRoot, "stable.txt"), "same");
        var changed = false;
        var progress = new SynchronousProgress(value =>
        {
            if (value.Stage != "Comparing file contents" || changed) return;
            File.WriteAllText(changingFile, "changed after measurement");
            changed = true;
        });

        var result = await new StorageAnalysisService().AnalyzeAsync(_testRoot, progress: progress);

        Assert.True(changed);
        Assert.Empty(result.DuplicateGroups);
        Assert.Equal(1, result.SkippedEntries);
    }

    [Fact]
    public async Task AnalyzeAsync_SkipsLinkedChildFoldersAndRejectsLinkedRoot()
    {
        var selected = Directory.CreateDirectory(Path.Combine(_testRoot, "selected")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(_testRoot, "outside")).FullName;
        await File.WriteAllTextAsync(Path.Combine(selected, "included.txt"), "inside");
        await File.WriteAllTextAsync(Path.Combine(outside, "excluded.txt"), "outside");
        var link = Path.Combine(selected, "linked-outside");
        if (OperatingSystem.IsWindows())
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
            {
                Arguments = $"/d /c mklink /J \"{link}\" \"{outside}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync());
        }
        else Directory.CreateSymbolicLink(link, outside);
        try
        {
            var result = await new StorageAnalysisService().AnalyzeAsync(selected);
            Assert.Equal(1, result.FileCount);
            Assert.Equal(6, result.TotalBytes);
            Assert.True(result.SkippedEntries >= 1);
            await Assert.ThrowsAsync<IOException>(() => new StorageAnalysisService().AnalyzeAsync(link));
            Assert.True(File.Exists(Path.Combine(outside, "excluded.txt")));
        }
        finally { Directory.Delete(link); }
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, recursive: true);
    }

    private sealed class SynchronousProgress(Action<StorageAnalysisProgress> callback) : IProgress<StorageAnalysisProgress>
    {
        public void Report(StorageAnalysisProgress value) => callback(value);
    }
}

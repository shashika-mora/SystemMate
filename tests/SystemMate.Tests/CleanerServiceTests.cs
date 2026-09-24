using Microsoft.Data.Sqlite;
using SystemMate.Models;
using SystemMate.Services;
using Xunit;

namespace SystemMate.Tests;

public sealed class CleanerServiceTests : IDisposable
{
    private readonly string _connectionString =
        $"Data Source=cleaner_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly TestHistoryService _history;
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"systemmate_test_{Guid.NewGuid():N}");

    public CleanerServiceTests()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        _history = new TestHistoryService(_connectionString);
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task CleanAsync_LogsOneAccurateDeletedOutcome()
    {
        var file = Path.Combine(_tempDirectory, "clean-me.tmp");
        await File.WriteAllTextAsync(file, "temporary");
        var scan = CreateScan("win_temp", file);

        var session = await new CleanerService(_history).CleanAsync(scan);
        var history = await _history.GetSessionsAsync();

        Assert.False(File.Exists(file));
        Assert.Equal(1, session.FilesDeleted);
        Assert.Single(session.Records);
        Assert.Equal(OperationStatus.Deleted, session.Records[0].Status);
        Assert.Single(history);
        Assert.Single(history[0].Records);
        Assert.Equal(OperationStatus.Deleted, history[0].Records[0].Status);
    }

    [Fact]
    public async Task CleanAsync_SkipsCandidateOutsideSelectedCategoryRoot()
    {
        var file = Path.Combine(_tempDirectory, "do-not-delete.tmp");
        await File.WriteAllTextAsync(file, "protected");
        var scan = CreateScan("browser_cache", file);

        var session = await new CleanerService(_history).CleanAsync(scan);
        var history = await _history.GetSessionsAsync();

        Assert.True(File.Exists(file));
        Assert.Equal(1, session.FilesSkipped);
        Assert.Equal(OperationStatus.Skipped, session.Records[0].Status);
        Assert.Contains("outside", session.Records[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(history[0].Records);
        Assert.Equal(OperationStatus.Skipped, history[0].Records[0].Status);
    }

    private static ScanResult CreateScan(string id, string file)
    {
        var result = new ScanResult();
        var category = new CleanupCategory
        {
            Id = id,
            DisplayName = id,
            Description = "test",
        };
        category.FilePaths.Add(file);
        category.FileCount = 1;
        category.IsSelected = true;
        result.Categories.Add(category);
        return result;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
        _history.Dispose();
        _keepAlive.Dispose();
    }

    private sealed class TestHistoryService : HistoryService
    {
        public TestHistoryService(string connectionString) : base(connectionString) { }
    }
}

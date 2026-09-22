using Microsoft.Data.Sqlite;
using SystemMate.Models;
using SystemMate.Services;
using Xunit;

namespace SystemMate.Tests;

/// <summary>
/// Tests for HistoryService using SQLite in-memory database.
/// Subclasses HistoryService to inject an in-memory connection.
/// </summary>
public sealed class HistoryServiceTests : IDisposable
{
    // Use a named in-memory SQLite database so multiple connections share the same instance
    private readonly string _connStr = $"Data Source=history_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAliveConn;
    private readonly TestHistoryService _svc;

    public HistoryServiceTests()
    {
        _keepAliveConn = new SqliteConnection(_connStr);
        _keepAliveConn.Open();
        _svc = new TestHistoryService(_connStr);
    }

    [Fact]
    public async Task LogOperation_InsertsRecord()
    {
        var record = MakeRecord("session1", OperationStatus.Deleted, 1024);
        _svc.LogOperation(record);

        var sessions = await _svc.GetSessionsAsync();
        Assert.Single(sessions);
        Assert.Single(sessions[0].Records);
        Assert.Equal(1024, sessions[0].Records[0].SizeBytes);
    }

    [Fact]
    public async Task LogOperation_MultipleRecords_AggregatesCorrectly()
    {
        _svc.LogOperation(MakeRecord("s1", OperationStatus.Deleted, 500_000));
        _svc.LogOperation(MakeRecord("s1", OperationStatus.Deleted, 300_000));
        _svc.LogOperation(MakeRecord("s1", OperationStatus.Error, 0));

        var sessions = await _svc.GetSessionsAsync();
        Assert.Single(sessions);
        var session = sessions[0];
        Assert.Equal(800_000, session.TotalBytesFreed);
        Assert.Equal(2, session.FilesDeleted);
        Assert.Equal(1, session.FilesErrored);
    }

    [Fact]
    public async Task ClearHistoryAsync_RemovesAllRecords()
    {
        _svc.LogOperation(MakeRecord("s1", OperationStatus.Deleted, 100));
        await _svc.ClearHistoryAsync();

        var sessions = await _svc.GetSessionsAsync();
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task ExportCsvAsync_CreatesFile()
    {
        _svc.LogOperation(MakeRecord("s1", OperationStatus.Deleted, 1024));
        var path = Path.Combine(Path.GetTempPath(), $"sm_test_{Guid.NewGuid():N}.csv");

        await _svc.ExportToCsvAsync(path);

        Assert.True(File.Exists(path));
        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("SessionId", content);
        Assert.Contains("s1", content);

        File.Delete(path);
    }

    private static OperationRecord MakeRecord(string sessionId, OperationStatus status, long size)
        => new()
        {
            SessionId    = sessionId,
            Timestamp    = DateTime.Now,
            Category     = "Test",
            FilePath     = $"C:\\Temp\\file_{Guid.NewGuid():N}.tmp",
            SizeBytes    = size,
            Status       = status,
        };

    public void Dispose()
    {
        _svc.Dispose();
        _keepAliveConn.Dispose();
    }

    // Testable subclass that uses injected connection string
    private sealed class TestHistoryService : HistoryService
    {
        public TestHistoryService(string connectionString)
            : base(connectionString) { }
    }
}

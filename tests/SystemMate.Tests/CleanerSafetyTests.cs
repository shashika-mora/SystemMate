using Microsoft.Data.Sqlite;
using SystemMate.Models;
using SystemMate.Services;
using Xunit;

namespace SystemMate.Tests;

public sealed class CleanerSafetyTests : IDisposable
{
    private readonly string _connectionString = $"Data Source=safety_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly TestHistoryService _history;
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"systemmate_safety_{Guid.NewGuid():N}");

    public CleanerSafetyTests()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        _history = new TestHistoryService(_connectionString);
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task FirefoxScanAndForgedCleanupPreserveProfileData()
    {
        var cache = WriteFixture("local/Mozilla/Firefox/Profiles/test.default/cache2/entries/cached");
        var password = WriteFixture("local/Mozilla/Firefox/Profiles/test.default/logins.json");
        var cookies = WriteFixture("local/Mozilla/Firefox/Profiles/test.default/cookies.sqlite");
        var cleaner = CreateCleaner();
        var scan = await cleaner.ScanAsync();
        var browser = scan.Categories.Single(c => c.Id == "browser_cache");
        Assert.Equal(new[] { cache }, browser.FilePaths);

        var forged = CreateScan("browser_cache", password, cookies);
        var skipped = await cleaner.CleanAsync(forged);
        Assert.Equal(2, skipped.FilesSkipped);
        Assert.True(File.Exists(password));
        Assert.True(File.Exists(cookies));
    }

    [Fact]
    public async Task ChromiumProfilesIncludeOnlyCacheFolders()
    {
        var cache = WriteFixture("local/Google/Chrome/User Data/Profile 2/Cache/Cache_Data/data_0");
        var codeCache = WriteFixture("local/Microsoft/Edge/User Data/Default/Code Cache/js/data_0");
        var passwords = WriteFixture("local/Google/Chrome/User Data/Profile 2/Login Data");
        var cookies = WriteFixture("local/Microsoft/Edge/User Data/Default/Network/Cookies");
        var scan = await CreateCleaner().ScanAsync();
        var browser = scan.Categories.Single(c => c.Id == "browser_cache");
        Assert.Equal(2, browser.FileCount);
        Assert.Contains(cache, browser.FilePaths);
        Assert.Contains(codeCache, browser.FilePaths);
        Assert.DoesNotContain(passwords, browser.FilePaths);
        Assert.DoesNotContain(cookies, browser.FilePaths);
    }

    [Fact]
    public async Task ThumbnailScanAndForgedCleanupRejectOtherExplorerFiles()
    {
        var thumbnail = WriteFixture("local/Microsoft/Windows/Explorer/thumbcache_96.db");
        var other = WriteFixture("local/Microsoft/Windows/Explorer/iconcache_96.db");
        var nested = WriteFixture("local/Microsoft/Windows/Explorer/nested/thumbcache_96.db");
        var cleaner = CreateCleaner();
        var scan = await cleaner.ScanAsync();
        Assert.Equal(new[] { thumbnail }, scan.Categories.Single(c => c.Id == "thumbnails").FilePaths);
        var session = await cleaner.CleanAsync(CreateScan("thumbnails", other, nested));
        Assert.Equal(2, session.FilesSkipped);
        Assert.True(File.Exists(other));
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public async Task ScanDeduplicatesOverlappingTemporaryRoots()
    {
        var file = WriteFixture("temp/nested/file.tmp");
        var cleaner = new CleanerService(_history, Path.Combine(_tempDirectory, "local"),
            Path.Combine(_tempDirectory, "roaming"),
            [Path.Combine(_tempDirectory, "temp"), Path.Combine(_tempDirectory, "temp", "nested")], _ => false);
        var scan = await cleaner.ScanAsync();
        var temporary = scan.Categories.Single(c => c.Id == "win_temp");
        Assert.Equal(new[] { file }, temporary.FilePaths);
        Assert.Equal(1, temporary.FileCount);
    }

    [Fact]
    public async Task CleanupSkipsBrowserThatStartsAfterScan()
    {
        var file = WriteFixture("local/Google/Chrome/User Data/Default/Cache/data_0");
        var running = false;
        var cleaner = CreateCleaner(_ => running);
        var scan = await cleaner.ScanAsync();
        running = true;
        var session = await cleaner.CleanAsync(scan);
        Assert.Equal(1, session.FilesSkipped);
        Assert.True(File.Exists(file));
        var busyScan = await cleaner.ScanAsync();
        var category = busyScan.Categories.Single(c => c.Id == "browser_cache");
        Assert.Empty(category.FilePaths);
        Assert.Contains("Close chrome", category.ScanNote);
    }

    [Fact]
    public async Task CleanupPersistsIntentBeforeDeletionAndCompletesTheSameRow()
    {
        var file = WriteFixture("temp/intent.tmp");
        _history.AfterBegin = record =>
        {
            Assert.True(File.Exists(file));
            using var command = _keepAlive.CreateCommand();
            command.CommandText = "SELECT Status FROM CleanupOperations WHERE Id = $id";
            command.Parameters.AddWithValue("$id", record.Id);
            Assert.Equal("Pending", command.ExecuteScalar());
        };
        await CreateCleaner().CleanAsync(CreateScan("win_temp", file));
        var history = Assert.Single(await _history.GetSessionsAsync());
        Assert.Equal(OperationStatus.Deleted, Assert.Single(history.Records).Status);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task CleanupNeverDeletesWhenIntentCannotBePersisted()
    {
        var file = WriteFixture("temp/keep.tmp");
        _history.FailBegin = true;
        var session = await CreateCleaner().CleanAsync(CreateScan("win_temp", file));
        Assert.True(File.Exists(file));
        Assert.Equal(0, session.FilesDeleted);
        Assert.Equal(1, session.FilesErrored);
    }

    [Fact]
    public async Task FailedOutcomePersistenceLeavesUnconfirmedIntent()
    {
        var file = WriteFixture("temp/pending.tmp");
        _history.FailComplete = true;
        await Assert.ThrowsAsync<IOException>(() => CreateCleaner().CleanAsync(CreateScan("win_temp", file)));
        var history = Assert.Single(await _history.GetSessionsAsync());
        Assert.Equal(1, history.FilesPending);
        Assert.Equal(0, history.FilesErrored);
        Assert.Equal(0, history.FilesDeleted);
        Assert.Equal(OperationStatus.Pending, Assert.Single(history.Records).Status);
    }

    [Fact]
    public async Task CanceledCleanupLeavesRemainingFilesUntouched()
    {
        var file = WriteFixture("temp/canceled.tmp");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var session = await CreateCleaner().CleanAsync(CreateScan("win_temp", file), cancellationToken: cancellation.Token);
        Assert.True(session.WasCanceled);
        Assert.True(File.Exists(file));
        Assert.Empty(session.Records);
    }

    private CleanerService CreateCleaner(Func<string, bool>? running = null) =>
        new(_history, Path.Combine(_tempDirectory, "local"), Path.Combine(_tempDirectory, "roaming"),
            [Path.Combine(_tempDirectory, "temp")], running ?? (_ => false));

    private string WriteFixture(string relativePath)
    {
        var path = Path.Combine(_tempDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "temporary test content");
        return path;
    }

    private static ScanResult CreateScan(string categoryId, params string[] files)
    {
        var scan = new ScanResult();
        var category = new CleanupCategory { Id = categoryId, DisplayName = categoryId, Description = "test", FileCount = files.Length };
        category.FilePaths.AddRange(files);
        scan.Categories.Add(category);
        return scan;
    }

    public void Dispose()
    {
        _history.Dispose();
        _keepAlive.Dispose();
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, true);
    }

    private sealed class TestHistoryService(string connectionString) : HistoryService(connectionString)
    {
        public Action<OperationRecord>? AfterBegin { get; set; }
        public bool FailBegin { get; set; }
        public bool FailComplete { get; set; }
        public override void BeginOperation(OperationRecord record)
        {
            if (FailBegin) throw new IOException("Test intent storage failure");
            base.BeginOperation(record);
            AfterBegin?.Invoke(record);
        }
        public override void CompleteOperation(OperationRecord record)
        {
            if (FailComplete) throw new IOException("Test outcome storage failure");
            base.CompleteOperation(record);
        }
    }
}

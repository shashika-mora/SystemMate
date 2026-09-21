using Microsoft.Data.Sqlite;
using SystemMate.Models;

namespace SystemMate.Services;

/// <summary>
/// Persists and retrieves cleanup operation records using SQLite.
/// Database lives in %LOCALAPPDATA%\SystemMate\history.db
/// </summary>
public sealed class HistoryService
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public HistoryService()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SystemMate");
        Directory.CreateDirectory(dataDir);
        _dbPath = Path.Combine(dataDir, "history.db");
        _connectionString = $"Data Source={_dbPath}";
        InitializeDatabase();
    }

    /// <summary>Internal constructor for unit tests — accepts a custom connection string.</summary>
    internal HistoryService(string connectionString)
    {
        _dbPath = string.Empty;
        _connectionString = connectionString;
        InitializeDatabase();
    }

    // ── Schema ───────────────────────────────────────────────────────────────

    private void InitializeDatabase()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS CleanupOperations (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId    TEXT    NOT NULL,
                Timestamp    TEXT    NOT NULL,
                Category     TEXT    NOT NULL,
                FilePath     TEXT    NOT NULL,
                SizeBytes    INTEGER NOT NULL DEFAULT 0,
                Status       TEXT    NOT NULL,
                ErrorMessage TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_session
                ON CleanupOperations(SessionId, Timestamp DESC);
            """;
        cmd.ExecuteNonQuery();
    }

    // ── Write ────────────────────────────────────────────────────────────────

    public void LogOperation(OperationRecord record)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO CleanupOperations
                (SessionId, Timestamp, Category, FilePath, SizeBytes, Status, ErrorMessage)
            VALUES
                ($sessionId, $timestamp, $category, $filePath, $sizeBytes, $status, $errorMessage)
            """;
        cmd.Parameters.AddWithValue("$sessionId",    record.SessionId);
        cmd.Parameters.AddWithValue("$timestamp",    record.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("$category",     record.Category);
        cmd.Parameters.AddWithValue("$filePath",     record.FilePath);
        cmd.Parameters.AddWithValue("$sizeBytes",    record.SizeBytes);
        cmd.Parameters.AddWithValue("$status",       record.Status.ToString());
        cmd.Parameters.AddWithValue("$errorMessage", record.ErrorMessage ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    // ── Read ─────────────────────────────────────────────────────────────────

    public async Task<List<CleanupSession>> GetSessionsAsync(int limit = 50)
    {
        return await Task.Run(() =>
        {
            var sessions = new Dictionary<string, CleanupSession>();

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();

            // Load the most recent N sessions ordered newest-first
            cmd.CommandText = """
                SELECT SessionId, Timestamp, Category, FilePath, SizeBytes, Status, ErrorMessage, Id
                FROM CleanupOperations
                WHERE SessionId IN (
                    SELECT DISTINCT SessionId FROM CleanupOperations
                    ORDER BY MIN(Timestamp) DESC
                    LIMIT $limit
                )
                ORDER BY Timestamp DESC
                """;
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var sessionId = reader.GetString(0);
                var timestamp = DateTime.Parse(reader.GetString(1));
                var status = Enum.TryParse<OperationStatus>(reader.GetString(5), out var s)
                    ? s : OperationStatus.Deleted;

                if (!sessions.TryGetValue(sessionId, out var session))
                {
                    session = new CleanupSession
                    {
                        SessionId  = sessionId,
                        StartedAt  = timestamp,
                    };
                    sessions[sessionId] = session;
                }

                var record = new OperationRecord
                {
                    Id           = reader.GetInt32(7),
                    SessionId    = sessionId,
                    Timestamp    = timestamp,
                    Category     = reader.GetString(2),
                    FilePath     = reader.GetString(3),
                    SizeBytes    = reader.GetInt64(4),
                    Status       = status,
                    ErrorMessage = reader.IsDBNull(6) ? null : reader.GetString(6),
                };
                session.Records.Add(record);

                if (status == OperationStatus.Deleted)
                {
                    session.TotalBytesFreed += record.SizeBytes;
                    session.FilesDeleted++;
                }
                else if (status == OperationStatus.Skipped)
                {
                    session.FilesSkipped++;
                }
                else
                {
                    session.FilesErrored++;
                }
            }

            return sessions.Values.OrderByDescending(s => s.StartedAt).ToList();
        });
    }

    public async Task ClearHistoryAsync()
    {
        await Task.Run(() =>
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM CleanupOperations";
            cmd.ExecuteNonQuery();
        });
    }

    public async Task ExportToCsvAsync(string filePath)
    {
        var sessions = await GetSessionsAsync(int.MaxValue);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("SessionId,Timestamp,Category,FilePath,SizeBytes,Status,Error");
        foreach (var session in sessions)
            foreach (var r in session.Records)
                sb.AppendLine($"\"{r.SessionId}\",\"{r.Timestamp:O}\",\"{r.Category}\"," +
                              $"\"{r.FilePath}\",{r.SizeBytes},\"{r.Status}\"," +
                              $"\"{r.ErrorMessage?.Replace("\"", "\"\"")}\"");
        await File.WriteAllTextAsync(filePath, sb.ToString());
    }
}

using System.IO;
using System.Globalization;
using Microsoft.Data.Sqlite;
using DesktopPuppy.Native;

namespace DesktopPuppy.Usage;

public sealed class UsageSqliteStore : IUsageEventSink, IDisposable
{
    static UsageSqliteStore()
    {
        SQLitePCL.Batteries_V2.Init();
    }
    private readonly object _initializationGate = new();
    private readonly string _databasePath;
    private bool _initialized;
    private bool _disposed;

    public UsageSqliteStore(string? databasePath = null)
    {
        _databasePath = string.IsNullOrWhiteSpace(databasePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopPuppy",
                "desktop-puppy.db")
            : databasePath;
    }

    public string DatabasePath => _databasePath;

    public void Initialize()
    {
        lock (_initializationGate)
        {
            ThrowIfDisposed();
            if (_initialized) return;

            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS usage_sessions (
                    id TEXT PRIMARY KEY NOT NULL,
                    window_handle INTEGER NOT NULL,
                    process_id INTEGER NOT NULL,
                    process_name TEXT NOT NULL,
                    window_title TEXT NOT NULL,
                    started_at_utc TEXT NOT NULL,
                    ended_at_utc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_usage_sessions_started_at_utc
                    ON usage_sessions (started_at_utc);
                CREATE TABLE IF NOT EXISTS window_title_intervals (
                    id TEXT PRIMARY KEY NOT NULL,
                    window_handle INTEGER NOT NULL,
                    process_id INTEGER NOT NULL,
                    process_name TEXT NOT NULL,
                    window_title TEXT NOT NULL,
                    started_at_utc TEXT NOT NULL,
                    ended_at_utc TEXT NULL,
                    last_confirmed_at_utc TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_window_title_intervals_started_at_utc
                    ON window_title_intervals (started_at_utc);
                """;
            command.ExecuteNonQuery();

            command.CommandText = "PRAGMA table_info(window_title_intervals);";
            bool hasCheckpointColumn = false;
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (reader.GetString(1) == "last_confirmed_at_utc") hasCheckpointColumn = true;
                }
            }
            if (!hasCheckpointColumn)
            {
                command.CommandText = "ALTER TABLE window_title_intervals ADD COLUMN last_confirmed_at_utc TEXT NULL;";
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            _initialized = true;
        }
    }

    /// <summary>
    /// Seal intervals left by a previous process. The single-instance collection owner must
    /// call this once at startup, before retention or collection; observers must not call it.
    /// </summary>
    public void RecoverAbandonedTitleIntervals()
    {
        ThrowIfDisposed();
        EnsureInitialized();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE window_title_intervals
            SET ended_at_utc = MAX(started_at_utc, COALESCE(last_confirmed_at_utc, started_at_utc))
            WHERE ended_at_utc IS NULL;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public void AppendSession(ForegroundSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfDisposed();
        if (session.EndedAt is null)
        {
            throw new ArgumentException("Only closed foreground sessions can be persisted.", nameof(session));
        }

        EnsureInitialized();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO usage_sessions (
                id,
                window_handle,
                process_id,
                process_name,
                window_title,
                started_at_utc,
                ended_at_utc)
            VALUES ($id, $windowHandle, $processId, $processName, $windowTitle, $startedAt, $endedAt);
            """;
        command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
        command.Parameters.AddWithValue("$windowHandle", session.Snapshot.WindowHandle.ToInt64());
        command.Parameters.AddWithValue("$processId", session.ProcessId);
        command.Parameters.AddWithValue("$processName", session.ProcessName);
        command.Parameters.AddWithValue("$windowTitle", session.WindowTitle);
        command.Parameters.AddWithValue("$startedAt", FormatInstant(session.StartedAt));
        command.Parameters.AddWithValue("$endedAt", FormatInstant(session.EndedAt.Value));
        command.ExecuteNonQuery();
    }

    public void UpsertWindowTitleInterval(WindowTitleInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        ThrowIfDisposed();
        EnsureInitialized();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO window_title_intervals (
                id,
                window_handle,
                process_id,
                process_name,
                window_title,
                started_at_utc,
                ended_at_utc,
                last_confirmed_at_utc)
            VALUES ($id, $windowHandle, $processId, $processName, $windowTitle, $startedAt, $endedAt, $lastConfirmedAt)
            ON CONFLICT(id) DO UPDATE SET
                window_handle = excluded.window_handle,
                process_id = excluded.process_id,
                process_name = excluded.process_name,
                window_title = excluded.window_title,
                started_at_utc = MAX(window_title_intervals.started_at_utc, excluded.started_at_utc),
                ended_at_utc = excluded.ended_at_utc,
                last_confirmed_at_utc = excluded.last_confirmed_at_utc;
            """;
        command.Parameters.AddWithValue("$id", interval.Id.ToString("D"));
        command.Parameters.AddWithValue("$windowHandle", interval.Snapshot.WindowHandle.ToInt64());
        command.Parameters.AddWithValue("$processId", interval.ProcessId);
        command.Parameters.AddWithValue("$processName", interval.ProcessName);
        command.Parameters.AddWithValue("$windowTitle", interval.WindowTitle);
        command.Parameters.AddWithValue("$startedAt", FormatInstant(interval.StartedAt));
        command.Parameters.AddWithValue(
            "$endedAt",
            interval.EndedAt is null ? DBNull.Value : FormatInstant(interval.EndedAt.Value));
        command.Parameters.AddWithValue(
            "$lastConfirmedAt",
            interval.LastConfirmedAt is { } confirmedAt ? FormatInstant(confirmedAt) : DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void PruneBefore(DateTimeOffset cutoffUtc)
    {
        ThrowIfDisposed();
        EnsureInitialized();

        var cutoff = FormatInstant(cutoffUtc);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        ExecuteRetentionCommand(connection, transaction, "DELETE FROM usage_sessions WHERE ended_at_utc <= $cutoff;", cutoff);
        ExecuteRetentionCommand(connection, transaction, "UPDATE usage_sessions SET started_at_utc = $cutoff WHERE started_at_utc < $cutoff AND ended_at_utc > $cutoff;", cutoff);
        ExecuteRetentionCommand(connection, transaction, "DELETE FROM window_title_intervals WHERE ended_at_utc IS NOT NULL AND ended_at_utc <= $cutoff;", cutoff);
        ExecuteRetentionCommand(connection, transaction, "UPDATE window_title_intervals SET started_at_utc = $cutoff WHERE started_at_utc < $cutoff AND (ended_at_utc IS NULL OR ended_at_utc > $cutoff);", cutoff);
        transaction.Commit();
    }
    public IReadOnlyList<ForegroundSession> ReadSessions()
    {
        ThrowIfDisposed();
        EnsureInitialized();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, window_handle, process_id, process_name, window_title,
                   started_at_utc, ended_at_utc
            FROM usage_sessions
            ORDER BY started_at_utc, id;
            """;

        using var reader = command.ExecuteReader();
        var sessions = new List<ForegroundSession>();
        while (reader.Read())
        {
            var snapshot = new ForegroundWindowSnapshot(
                (nint)reader.GetInt64(1),
                checked((uint)reader.GetInt64(2)),
                reader.GetString(3),
                reader.GetString(4));
            sessions.Add(new ForegroundSession(
                Guid.Parse(reader.GetString(0)),
                snapshot,
                ParseInstant(reader.GetString(5)),
                ParseInstant(reader.GetString(6))));
        }

        return sessions;
    }

    public IReadOnlyList<WindowTitleInterval> ReadWindowTitleIntervals()
    {
        ThrowIfDisposed();
        EnsureInitialized();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, window_handle, process_id, process_name, window_title,
                   started_at_utc, ended_at_utc, last_confirmed_at_utc
            FROM window_title_intervals
            ORDER BY started_at_utc, id;
            """;

        using var reader = command.ExecuteReader();
        var intervals = new List<WindowTitleInterval>();
        while (reader.Read())
        {
            var snapshot = new ForegroundWindowSnapshot(
                (nint)reader.GetInt64(1),
                checked((uint)reader.GetInt64(2)),
                reader.GetString(3),
                reader.GetString(4));
            intervals.Add(new WindowTitleInterval(
                Guid.Parse(reader.GetString(0)),
                snapshot,
                ParseInstant(reader.GetString(5)),
                reader.IsDBNull(6) ? null : ParseInstant(reader.GetString(6)))
            {
                LastConfirmedAt = reader.IsDBNull(7) ? null : ParseInstant(reader.GetString(7))
            });
        }

        return intervals;
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private static void ExecuteRetentionCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        string cutoff)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.Parameters.AddWithValue("$cutoff", cutoff);
        command.ExecuteNonQuery();
    }
    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            Initialize();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static string FormatInstant(DateTimeOffset instant)
    {
        return instant.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseInstant(string value)
    {
        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).ToUniversalTime();
    }
}

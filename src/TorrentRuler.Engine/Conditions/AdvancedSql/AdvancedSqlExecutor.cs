using Microsoft.Data.Sqlite;
using TorrentRuler.Snapshot;

namespace TorrentRuler.Engine.Conditions.AdvancedSql;

/// <summary>
/// Runs power-user-authored raw SQL (Rule.AdvancedSql) against the snapshot. The author writes
/// a complete query returning <c>instance_id</c> and <c>torrent_hash</c>; the engine wraps it as
/// <c>SELECT DISTINCT instance_id, torrent_hash FROM ( … )</c>, which both dedupes the output
/// and is what holds the query to that contract.
///
/// The threat model here is a mistake by the app's own admin, not an external attacker
/// (only an authenticated admin can ever author a rule) -- so the goal is to stop a typo
/// from corrupting the snapshot or hanging the app, not to sandbox a hostile author:
///   - a second connection to the same shared-cache in-memory DB with PRAGMA query_only
///     set (SQLite's own engine rejects any write statement on that connection with
///     SQLITE_READONLY -- this is enforced by SQLite itself, not application code, though
///     note it's a per-connection pragma, not an OS-level read-only file open: a
///     shared-cache in-memory database can only be addressed via mode=memory, which
///     can't be combined with the URI mode=ro flag, so query_only is the mechanism SQLite
///     actually offers for this)
///   - <see cref="SqlGuard"/>: single-statement, SELECT/WITH only, and a keyword denylist that
///     ignores string literals and comments
///   - a row cap enforced by the read loop itself (not just a LIMIT clause the author
///     could omit)
///   - a cooperative-cancellation timeout on execution
///   - EXPLAIN QUERY PLAN and a LIMIT 0 column probe run at save time, so a broken query is
///     caught before it's persisted, not at the next scheduled run
/// </summary>
public class AdvancedSqlExecutor
{
    public const int MaxRows = 50_000;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public AdvancedSqlValidationResult Validate(SnapshotDatabase snapshot, string rawSql) =>
        Validate(snapshot, rawSql, FieldResolutionContext.Lenient);

    public AdvancedSqlValidationResult Validate(SnapshotDatabase snapshot, string rawSql, FieldResolutionContext resolution)
    {
        using var readOnly = OpenReadOnly(snapshot);
        var (error, userSql, _) = Prepare(readOnly, rawSql, resolution);
        return error is null ? AdvancedSqlValidationResult.Ok(Wrap(userSql!)) : AdvancedSqlValidationResult.Failure(error);
    }

    /// <summary>
    /// What the editor's Validate button shows: the query's own columns and first
    /// <see cref="AdvancedSqlPreview.SampleSize"/> rows (extra columns included), how many distinct
    /// torrents it matches, and how many of those pairs name no torrent in the snapshot.
    /// </summary>
    public async Task<AdvancedSqlPreview> PreviewAsync(SnapshotDatabase snapshot, string rawSql, FieldResolutionContext resolution, CancellationToken ct = default)
    {
        using var readOnly = OpenReadOnly(snapshot);
        var (error, userSql, columns) = Prepare(readOnly, rawSql, resolution);
        if (error is not null)
        {
            return AdvancedSqlPreview.Failure(error);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultTimeout);

        try
        {
            var rows = new List<object?[]>();
            using (var sample = readOnly.CreateCommand())
            {
                sample.CommandText = $"SELECT * FROM (\n{userSql}\n) LIMIT {AdvancedSqlPreview.SampleSize}";
                await using var reader = await sample.ExecuteReaderAsync(cts.Token);
                while (await reader.ReadAsync(cts.Token))
                {
                    var row = new object?[reader.FieldCount];
                    for (var i = 0; i < row.Length; i++)
                    {
                        row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    rows.Add(row);
                }
            }

            using var counts = readOnly.CreateCommand();
            counts.CommandText = $"""
                SELECT COUNT(*), COALESCE(SUM(q.hash IS NULL), 0)
                FROM ({Wrap(userSql!)}) w
                LEFT JOIN qbittorrent q ON q.instance_id = w.instance_id AND q.hash = w.torrent_hash
                """;
            await using var countReader = await counts.ExecuteReaderAsync(cts.Token);
            await countReader.ReadAsync(cts.Token);

            return new AdvancedSqlPreview(true, null, columns!, rows, countReader.GetInt32(0), countReader.GetInt32(1));
        }
        catch (SqliteException ex)
        {
            return AdvancedSqlPreview.Failure($"Invalid SQL: {ex.Message}");
        }
    }

    public async Task<List<MatchedTorrent>> ExecuteAsync(SnapshotDatabase snapshot, string compiledSql, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        using var readOnly = OpenReadOnly(snapshot);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? DefaultTimeout);

        using var command = readOnly.CreateCommand();
        command.CommandText = compiledSql;

        var results = new List<MatchedTorrent>();
        await using var reader = await command.ExecuteReaderAsync(cts.Token);
        var instanceIdOrdinal = reader.GetOrdinal("instance_id");
        var torrentHashOrdinal = reader.GetOrdinal("torrent_hash");

        while (results.Count < MaxRows && await reader.ReadAsync(cts.Token))
        {
            // SQLite columns are dynamically typed, so an author's query can hand back a NULL hash
            // or a text instance id; such a row names no torrent and is skipped rather than thrown on.
            if (reader.GetValue(instanceIdOrdinal) is long instanceId
                && reader.GetValue(torrentHashOrdinal) is string hash)
            {
                results.Add(new MatchedTorrent((int)instanceId, hash));
            }
        }

        return results;
    }

    /// <summary>The engine's wrapper. The newlines matter: a query ending in a -- comment would otherwise swallow the closing paren.</summary>
    private static string Wrap(string userSql) => $"SELECT DISTINCT instance_id, torrent_hash FROM (\n{userSql}\n)";

    /// <summary>
    /// Guard, expand field keys, and probe: EXPLAIN the wrapped query and read the user query's
    /// own column list. Returns the expanded user SQL (without its trailing ';') and its columns,
    /// or an error message.
    /// </summary>
    private static (string? Error, string? UserSql, IReadOnlyList<string>? Columns) Prepare(
        SqliteConnection readOnly, string rawSql, FieldResolutionContext resolution)
    {
        if (SqlGuard.Check(rawSql) is { } shapeError)
        {
            return (shapeError, null, null);
        }

        string userSql;
        bool expandedAnyKey;
        try
        {
            // One pass rewrites every <type>.<instance>.<field> key into the SQL the structured
            // compiler emits, so a key copied from the Field reference panel works verbatim here.
            userSql = SqlGuard.StripTrailingSemicolon(SourceFieldExpander.Expand(rawSql, resolution, out expandedAnyKey));
        }
        catch (ConditionCompileException ex)
        {
            return (ex.Message, null, null);
        }

        try
        {
            List<string> columns;
            using (var probe = readOnly.CreateCommand())
            {
                probe.CommandText = $"SELECT * FROM (\n{userSql}\n) LIMIT 0";
                using var reader = probe.ExecuteReader();
                columns = [.. Enumerable.Range(0, reader.FieldCount).Select(reader.GetName)];
            }

            if (!columns.Contains("instance_id", StringComparer.OrdinalIgnoreCase)
                || !columns.Contains("torrent_hash", StringComparer.OrdinalIgnoreCase))
            {
                return ($"Query must return columns instance_id and torrent_hash; got: {string.Join(", ", columns)}", null, null);
            }

            using (var explain = readOnly.CreateCommand())
            {
                explain.CommandText = $"EXPLAIN QUERY PLAN {Wrap(userSql)}";
                using var reader = explain.ExecuteReader();
                while (reader.Read())
                {
                }
            }

            return (null, userSql, columns);
        }
        catch (SqliteException ex)
        {
            var message = $"Invalid SQL: {ex.Message}";
            if (expandedAnyKey && ex.Message.Contains("no such column: t.", StringComparison.OrdinalIgnoreCase))
            {
                message += " Field keys refer to the torrent as alias t, so the query needs FROM qbittorrent t.";
            }
            return (message, null, null);
        }
    }

    /// <summary>Internal (not private) so tests can verify the SQLite-level read-only guarantee directly, independent of the regex-based shape check.</summary>
    internal static SqliteConnection OpenReadOnly(SnapshotDatabase snapshot) => snapshot.OpenReadOnlyConnection();
}

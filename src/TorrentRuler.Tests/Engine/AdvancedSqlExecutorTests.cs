using Microsoft.Data.Sqlite;
using TorrentRuler.Core.Domain;
using TorrentRuler.Core.Domain.SourceData;
using TorrentRuler.Engine.Conditions;
using TorrentRuler.Engine.Conditions.AdvancedSql;
using TorrentRuler.Snapshot;
using Xunit;

namespace TorrentRuler.Tests.Engine;

public class AdvancedSqlExecutorTests : IDisposable
{
    private readonly SnapshotDatabase _db = new();
    private readonly AdvancedSqlExecutor _executor = new();

    private static readonly FieldResolutionContext Configured = FieldResolutionContext.For(
        [new InstanceRef(1, "qbt1", SourceType.Qbittorrent), new InstanceRef(2, "taut1", SourceType.Tautulli)],
        ["downloads"]);

    public void Dispose() => _db.Dispose();

    /// <summary>A WHERE expression as the full query a rule stores (what the migration produces).</summary>
    private static string Q(string where) => AdvancedSqlTemplate.FromLegacyWhere(where)!;

    private void SeedTwo() => _db.Rebuild(new SnapshotInput
    {
        Torrents =
        [
            new TorrentRecord { InstanceId = 1, InstanceName = "qbt", Hash = "aaa", Name = "A", SizeBytes = 1, Progress = 1 },
            new TorrentRecord { InstanceId = 1, InstanceName = "qbt", Hash = "bbb", Name = "B", SizeBytes = 1, Progress = 1 }
        ]
    });

    private async Task<List<MatchedTorrent>> RunAsync(string sql, FieldResolutionContext? resolution = null)
    {
        var validation = _executor.Validate(_db, sql, resolution ?? FieldResolutionContext.Lenient);
        Assert.True(validation.IsValid, validation.ErrorMessage);
        return await _executor.ExecuteAsync(_db, validation.CompiledSql!);
    }

    [Fact]
    public async Task Validate_WrapsAndDedupes()
    {
        SeedTwo();
        var matches = await RunAsync(
            "SELECT t.instance_id, t.hash AS torrent_hash, t.name FROM qbittorrent t UNION ALL SELECT t.instance_id, t.hash, t.name FROM qbittorrent t");
        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public async Task Validate_TrailingLineComment_StillRuns()
    {
        SeedTwo();
        var matches = await RunAsync("SELECT t.instance_id, t.hash AS torrent_hash FROM qbittorrent t -- all of them");
        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public async Task Validate_SemicolonBeforeTrailingComment_StillRuns()
    {
        SeedTwo();
        var matches = await RunAsync("SELECT t.instance_id, t.hash AS torrent_hash FROM qbittorrent t; -- done");
        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public async Task Validate_WithCte_AndUpperCaseColumn_Runs()
    {
        SeedTwo();
        var matches = await RunAsync("WITH big AS (SELECT * FROM qbittorrent) SELECT instance_id, hash AS TORRENT_HASH FROM big");
        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void Validate_MissingColumns_ListsWhatItGot()
    {
        var result = _executor.Validate(_db, "SELECT t.hash, t.name FROM qbittorrent t");
        Assert.False(result.IsValid);
        Assert.Equal("Query must return columns instance_id and torrent_hash; got: hash, name", result.ErrorMessage);
    }

    [Fact]
    public void Validate_FieldKeyWithoutAliasT_ExplainsAlias()
    {
        var result = _executor.Validate(_db, "SELECT instance_id, hash AS torrent_hash FROM qbittorrent WHERE qbittorrent.*.size_gb > 0");
        Assert.False(result.IsValid);
        Assert.Contains("FROM qbittorrent t", result.ErrorMessage);
    }

    [Fact]
    public async Task Validate_FieldKeysExpandAgainstT()
    {
        SeedTwo();
        var matches = await RunAsync("SELECT t.instance_id, t.hash AS torrent_hash FROM qbittorrent t WHERE qbittorrent.*.size_gb >= 0");
        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void Validate_NewRuleTemplate_IsValid() =>
        Assert.True(_executor.Validate(_db, AdvancedSqlTemplate.NewRule).IsValid);

    [Fact]
    public async Task Execute_SkipsRowsWithNullHashOrNonIntegerInstance()
    {
        SeedTwo();
        var matches = await RunAsync(
            "SELECT t.instance_id, t.hash AS torrent_hash FROM qbittorrent t UNION ALL SELECT 1, NULL UNION ALL SELECT 'x', 'ccc'");
        Assert.Equal(["aaa", "bbb"], matches.Select(m => m.TorrentHash).Order());
    }

    [Fact]
    public async Task Preview_ReportsColumnsSampleCountsAndUnknownPairs()
    {
        SeedTwo();
        var preview = await _executor.PreviewAsync(_db,
            "SELECT t.instance_id, t.hash AS torrent_hash, t.name FROM qbittorrent t UNION ALL SELECT 9, 'zzz', 'ghost'",
            FieldResolutionContext.Lenient);

        Assert.True(preview.IsValid, preview.Error);
        Assert.Equal(["instance_id", "torrent_hash", "name"], preview.Columns);
        Assert.Equal(3, preview.SampleRows.Count);
        Assert.Equal(3, preview.PairCount);
        Assert.Equal(1, preview.UnknownPairCount);
    }

    [Fact]
    public async Task Preview_CapsSampleAtTwentyRows()
    {
        _db.Rebuild(new SnapshotInput
        {
            Torrents = [.. Enumerable.Range(0, 25).Select(i =>
                new TorrentRecord { InstanceId = 1, InstanceName = "qbt", Hash = $"h{i}", Name = $"T{i}", SizeBytes = 1, Progress = 1 })]
        });

        var preview = await _executor.PreviewAsync(_db, AdvancedSqlTemplate.NewRule, FieldResolutionContext.Lenient);

        Assert.Equal(AdvancedSqlPreview.SampleSize, preview.SampleRows.Count);
        Assert.Equal(25, preview.PairCount);
        Assert.Equal(0, preview.UnknownPairCount);
    }

    [Fact]
    public async Task Preview_InvalidSql_ReportsError()
    {
        var preview = await _executor.PreviewAsync(_db, "SELECT nope FROM nowhere", FieldResolutionContext.Lenient);
        Assert.False(preview.IsValid);
        Assert.NotNull(preview.Error);
    }

    [Fact]
    public void Validate_RejectsEmptySql()
    {
        var result = _executor.Validate(_db, Q("   "));
        Assert.False(result.IsValid);
        Assert.Contains("empty", result.ErrorMessage);
    }

    [Fact]
    public void Validate_RejectsMultipleStatements()
    {
        var result = _executor.Validate(_db, Q("1=1; DROP TABLE qbittorrent"));
        Assert.False(result.IsValid);
        Assert.Contains("single statement", result.ErrorMessage);
    }

    [Fact]
    public void Validate_RejectsForbiddenKeyword()
    {
        var result = _executor.Validate(_db, Q("1=1 OR (SELECT 1 WHERE 1 IN (DROP))"));
        Assert.False(result.IsValid);
        Assert.Contains("DROP", result.ErrorMessage);
    }

    [Fact]
    public void Validate_CatchesInvalidSql_ViaExplainQueryPlan()
    {
        var result = _executor.Validate(_db, Q("this_column_does_not_exist = 1"));
        Assert.False(result.IsValid);
        Assert.Contains("Invalid SQL", result.ErrorMessage);
    }

    [Fact]
    public void Validate_WhereClauseMode_AcceptsSimplePredicate()
    {
        var result = _executor.Validate(_db, Q("qbittorrent.*.category = 'linux'"));
        Assert.True(result.IsValid);
        Assert.Contains("FROM qbittorrent t\nWHERE (t.category) = 'linux'", result.CompiledSql);
    }

    [Fact]
    public void Validate_AcceptsSnapshotUdf_OnReadOnlyConnection()
    {
        // days_since / size_gb / path_matches are per-connection functions; advanced SQL must
        // see them on the hardened connection, not only the read-write one.
        var result = _executor.Validate(_db, Q("size_gb(qbittorrent.*.size_bytes) > 5"));
        Assert.True(result.IsValid, result.ErrorMessage);
    }

    [Fact]
    public void Validate_AcceptsRegexp_OnReadOnlyConnection()
    {
        // regexp is a per-connection UDF; advanced SQL must see it on the hardened connection.
        var result = _executor.Validate(_db, Q("t.name REGEXP '(?i)s\\d{2}e\\d{2}'"));
        Assert.True(result.IsValid, result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_WithRegexp_ReturnsOnlyMatchingRows()
    {
        _db.Rebuild(new SnapshotInput
        {
            Torrents =
            [
                new TorrentRecord { InstanceId = 1, InstanceName = "qbt", Hash = "h1", Name = "Show.S01E02.1080p.mkv", Category = "tv", SizeBytes = 1, Progress = 1 },
                new TorrentRecord { InstanceId = 1, InstanceName = "qbt", Hash = "h2", Name = "Movie.2026.1080p.mkv", Category = "movies", SizeBytes = 1, Progress = 1 }
            ]
        });

        var validation = _executor.Validate(_db, Q("t.name REGEXP 's\\d{2}e\\d{2}'"));
        Assert.True(validation.IsValid, validation.ErrorMessage);

        var matches = await _executor.ExecuteAsync(_db, validation.CompiledSql!);

        var match = Assert.Single(matches);
        Assert.Equal("h1", match.TorrentHash);
    }

    [Fact]
    public void Validate_ExpandsStorageField_IntoScalarSubquery()
    {
        var result = _executor.Validate(_db, Q("storage.downloads.used_percent > 85"));
        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Contains("(SELECT storage.used_percent FROM storage WHERE instance = 'downloads') > 85", result.CompiledSql);
    }

    [Fact]
    public void Validate_ExpandsComputedStorageField_UsingSizeGbUdf()
    {
        var result = _executor.Validate(_db, Q("storage.downloads.free_gb < 100"));
        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Contains("(SELECT size_gb(storage.free_bytes) FROM storage WHERE instance = 'downloads') < 100", result.CompiledSql);
    }

    [Fact]
    public void Validate_ExpandsComputedFieldKey_FromTheFieldReference()
    {
        var result = _executor.Validate(_db, Q("qbittorrent.*.active_days >= 14"));
        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Contains("(t.active_time_seconds / 86400.0) >= 14", result.CompiledSql);
    }

    [Fact]
    public void Validate_ExpandsComputedFieldKey_AlongsideAStorageField()
    {
        // The exact shape from the bug report: a storage field and a computed torrent key in
        // one predicate. active_days used to fall through to SQLite as "no such column".
        var result = _executor.Validate(_db, Q("storage.downloads.used_percent < 90 and qbittorrent.*.active_days >= 14"));
        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Contains("(SELECT storage.used_percent FROM storage WHERE instance = 'downloads') < 90", result.CompiledSql);
        Assert.Contains("(t.active_time_seconds / 86400.0) >= 14", result.CompiledSql);
    }

    [Fact]
    public async Task ExecuteAsync_ComputedFieldKey_MatchesExpectedTorrents()
    {
        _db.Rebuild(new SnapshotInput
        {
            Torrents =
            [
                new TorrentRecord { InstanceId = 1, InstanceName = "qbt", Hash = "old", Name = "A", SizeBytes = 1, Progress = 1, ActiveTimeSeconds = 30 * 86400 },
                new TorrentRecord { InstanceId = 1, InstanceName = "qbt", Hash = "new", Name = "B", SizeBytes = 1, Progress = 1, ActiveTimeSeconds = 3 * 86400 }
            ]
        });

        var validation = _executor.Validate(_db, Q("qbittorrent.*.active_days >= 14"));
        Assert.True(validation.IsValid, validation.ErrorMessage);

        var matches = await _executor.ExecuteAsync(_db, validation.CompiledSql!);

        var match = Assert.Single(matches);
        Assert.Equal("old", match.TorrentHash);
    }

    [Fact]
    public void Validate_RejectsUnknownStorageAttribute()
    {
        var result = _executor.Validate(_db, Q("storage.downloads.bogus > 1"));
        Assert.False(result.IsValid);
        Assert.Contains("Unknown field 'bogus'", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_StorageField_MatchesEveryTorrentWhenThresholdCrossed()
    {
        _db.Rebuild(new SnapshotInput
        {
            Torrents =
            [
                new TorrentRecord { InstanceId = 1, InstanceName = "qbt", Hash = "h1", Name = "A", SizeBytes = 1, Progress = 1 },
                new TorrentRecord { InstanceId = 1, InstanceName = "qbt", Hash = "h2", Name = "B", SizeBytes = 1, Progress = 1 }
            ],
            StoragePaths =
            [
                new StorageUsageRecord { StoragePathId = 1, Name = "downloads", Path = "/downloads", Available = true, UsedPercent = 92.0, TotalBytes = 100, UsedBytes = 92, FreeBytes = 8 }
            ]
        });

        var validation = _executor.Validate(_db, Q("storage.downloads.used_percent > 85"));
        Assert.True(validation.IsValid, validation.ErrorMessage);

        var matches = await _executor.ExecuteAsync(_db, validation.CompiledSql!);

        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void Validate_FullQueryMode_RejectsQueryMissingRequiredColumns()
    {
        var result = _executor.Validate(_db, "SELECT hash FROM qbittorrent");
        Assert.False(result.IsValid);
        Assert.Contains("instance_id", result.ErrorMessage);
        Assert.Contains("torrent_hash", result.ErrorMessage);
    }

    [Fact]
    public void Validate_FullQueryMode_AcceptsQueryWithRequiredColumns()
    {
        var result = _executor.Validate(_db, "SELECT instance_id, hash AS torrent_hash FROM qbittorrent WHERE category = 'linux'");
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsMatchingRows()
    {
        _db.Rebuild(new SnapshotInput
        {
            Torrents =
            [
                new TorrentRecord { InstanceId = 1, InstanceName = "qbt", Hash = "h1", Name = "A", Category = "linux", SizeBytes = 1, Progress = 1 },
                new TorrentRecord { InstanceId = 1, InstanceName = "qbt", Hash = "h2", Name = "B", Category = "tv", SizeBytes = 1, Progress = 1 }
            ]
        });

        var validation = _executor.Validate(_db, Q("qbittorrent.*.category = 'linux'"));
        Assert.True(validation.IsValid);

        var matches = await _executor.ExecuteAsync(_db, validation.CompiledSql!);

        var match = Assert.Single(matches);
        Assert.Equal("h1", match.TorrentHash);
    }

    [Fact]
    public void OpenReadOnly_ActuallyRejectsWrites_AtTheSqliteLevel()
    {
        using var readOnly = AdvancedSqlExecutor.OpenReadOnly(_db);
        using var command = readOnly.CreateCommand();
        command.CommandText = "DELETE FROM qbittorrent";

        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }

    [Fact]
    public void OpenReadOnly_StillAllowsReadsAgainstTheSameSnapshot()
    {
        _db.Rebuild(new SnapshotInput
        {
            Torrents = [new TorrentRecord { InstanceId = 1, InstanceName = "qbt", Hash = "h1", Name = "A", SizeBytes = 1, Progress = 1 }]
        });

        using var readOnly = AdvancedSqlExecutor.OpenReadOnly(_db);
        using var command = readOnly.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM qbittorrent";

        Assert.Equal(1L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void Validate_ExpandsAggregateKey_IntoACorrelatedSubquery()
    {
        var result = _executor.Validate(_db, Q("tautulli.*.play_count = 0"), Configured);

        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Contains("(SELECT COUNT(*) FROM tautulli x0 WHERE x0.path_key = t.path_key AND x0.kind = 'history') = 0",
            result.CompiledSql);
    }

    [Fact]
    public void Validate_NamedTorrentInstance_YieldsNullOnOtherInstances()
    {
        var result = _executor.Validate(_db, Q("qbittorrent.qbt1.ratio > 1"), Configured);

        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Contains("(CASE WHEN t.instance = 'qbt1' THEN t.ratio END) > 1", result.CompiledSql);
    }

    [Fact]
    public void Validate_RejectsPerRowFieldOfARelatedSource()
    {
        // A related source has many rows per torrent, so its row fields have no scalar value in
        // hand-written SQL. The error has to say that rather than let SQLite say "no such column".
        var result = _executor.Validate(_db, Q("tautulli.*.watched_at > '2026-01-01'"), Configured);

        Assert.False(result.IsValid);
        Assert.Contains("per-row field", result.ErrorMessage);
        Assert.Contains("play_count", result.ErrorMessage);
    }

    [Fact]
    public void Validate_RejectsUnknownInstance_NamingTheConfiguredOnes()
    {
        var result = _executor.Validate(_db, Q("tautulli.nope.play_count = 0"), Configured);

        Assert.False(result.IsValid);
        Assert.Contains("taut1", result.ErrorMessage);
    }

    [Fact]
    public void Validate_LeavesAliasQualifiedColumnsAlone()
    {
        // "t.category" has a dot but isn't a field key, so the author's own qualified column
        // must survive untouched.
        var result = _executor.Validate(_db, Q("t.category = 'linux'"), Configured);

        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Contains("WHERE t.category = 'linux'", result.CompiledSql);
    }

    [Fact]
    public void Validate_LeavesThreeSegmentNamesAloneUnlessTheyStartWithASourceType()
    {
        // main.qbittorrent.ratio is schema.table.column, not a field key. Proof that it is left
        // alone is that SQLite -- not the expander -- is what rejects it.
        var result = _executor.Validate(_db, Q("main.qbittorrent.ratio > 1"), Configured);

        Assert.False(result.IsValid);
        Assert.Contains("no such column: main.qbittorrent.ratio", result.ErrorMessage);
    }

    [Fact]
    public void Validate_IgnoresKeysInsideStringLiteralsAndComments()
    {
        var result = _executor.Validate(
            _db,
            Q("/* qbittorrent.*.ratio */ qbittorrent.*.name = 'qbittorrent.*.ratio'"),
            Configured);

        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Contains("/* qbittorrent.*.ratio */ (t.name) = 'qbittorrent.*.ratio'", result.CompiledSql);
        Assert.DoesNotContain("(t.ratio)", result.CompiledSql);
    }

    [Fact]
    public async Task ExecuteAsync_AggregateKey_FindsNeverWatchedTorrents()
    {
        _db.Rebuild(new SnapshotInput
        {
            Torrents =
            [
                new TorrentRecord { InstanceId = 1, InstanceName = "qbt1", Hash = "watched", Name = "A", ContentPath = "/media/a.mkv", SizeBytes = 1, Progress = 1 },
                new TorrentRecord { InstanceId = 1, InstanceName = "qbt1", Hash = "never", Name = "B", ContentPath = "/media/b.mkv", SizeBytes = 1, Progress = 1 }
            ],
            WatchHistory =
            [
                new WatchHistoryRecord
                {
                    InstanceId = 2, InstanceName = "taut1", SourceType = SourceType.Tautulli,
                    FilePath = "/media/a.mkv", UserName = "alice", WatchedAt = DateTimeOffset.UtcNow
                }
            ]
        });

        var validation = _executor.Validate(_db, Q("tautulli.*.play_count = 0"), Configured);
        Assert.True(validation.IsValid, validation.ErrorMessage);

        var matches = await _executor.ExecuteAsync(_db, validation.CompiledSql!);

        Assert.Equal("never", Assert.Single(matches).TorrentHash);
    }
}

namespace TorrentRuler.Engine.Conditions.AdvancedSql;

/// <summary>
/// Result of <see cref="AdvancedSqlExecutor.PreviewAsync"/>: the query's own columns and first
/// rows, the number of distinct (instance_id, torrent_hash) pairs it returns, and how many of
/// those name no torrent in the snapshot (a non-blocking warning: the rule would just skip them).
/// </summary>
public sealed record AdvancedSqlPreview(
    bool IsValid,
    string? Error,
    IReadOnlyList<string> Columns,
    IReadOnlyList<object?[]> SampleRows,
    int PairCount,
    int UnknownPairCount)
{
    public const int SampleSize = 20;

    public static AdvancedSqlPreview Failure(string error) => new(false, error, [], [], 0, 0);
}

namespace TorrentRuler.Engine;

public interface IRuleRunner
{
    /// <summary>Refreshes needed sources, rebuilds a snapshot, evaluates the rule's condition, applies its actions, and persists a RunRecord. Never throws -- any failure is captured as a Failed RunRecord.</summary>
    Task RunAsync(int ruleId, CancellationToken ct = default);

    /// <summary>
    /// Evaluates a rule draft against a fresh snapshot and reports what it would match and do,
    /// without applying anything or writing a RunRecord. Never throws -- failures come back on
    /// <see cref="RulePreview.Error"/>.
    /// </summary>
    Task<RulePreview> DryRunAsync(RuleDraft draft, CancellationToken ct = default);

    /// <summary>Builds a fresh snapshot from the cached source data (refreshing stale sources first). Caller owns disposal. Used by the SQL sandbox.</summary>
    Task<TorrentRuler.Snapshot.SnapshotDatabase> BuildSandboxSnapshotAsync(CancellationToken ct = default);
}

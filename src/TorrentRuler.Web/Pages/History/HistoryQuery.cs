using TorrentRuler.Core.Domain;

namespace TorrentRuler.Web.Pages.History;

/// <summary>The History page's filters, as bound from the query string.</summary>
/// <param name="From">Start date (inclusive), from the date picker; interpreted as UTC.</param>
/// <param name="To">End date (inclusive, the whole day), from the date picker; interpreted as UTC.</param>
/// <param name="Range">Quick range "1h" | "24h" | "7d"; when set it overrides From/To.</param>
public sealed record HistoryFilter(
    int? RuleId = null,
    RunOutcome? Outcome = null,
    DateTime? From = null,
    DateTime? To = null,
    string? Range = null,
    bool AppliedOnly = false,
    bool FailuresOnly = false);

/// <summary>A run of consecutive, uneventful runs of one rule, shown as one collapsible row.</summary>
public sealed record HistoryGroup(IReadOnlyList<RunRecord> Runs)
{
    public RunRecord First => Runs[0];
    public bool IsCollapsed => Runs.Count > 1;
}

/// <summary>
/// Filtering, paging and grouping for the History page. Works on an in-memory list: SQLite's EF Core
/// provider can't translate DateTimeOffset comparisons, so the page loads a bounded recent window
/// (newest first) and narrows it here.
/// </summary>
public static class HistoryQuery
{
    public static readonly IReadOnlyList<(string Key, string Label, TimeSpan Span)> Ranges =
        [("1h", "1h", TimeSpan.FromHours(1)), ("24h", "24h", TimeSpan.FromHours(24)), ("7d", "7d", TimeSpan.FromDays(7))];

    public static List<RunRecord> Apply(IEnumerable<RunRecord> runs, HistoryFilter f, DateTimeOffset now)
    {
        var q = runs;
        if (f.RuleId is { } ruleId)
        {
            q = q.Where(r => r.RuleId == ruleId);
        }
        if (f.Outcome is { } outcome)
        {
            q = q.Where(r => r.Outcome == outcome);
        }

        if (Ranges.FirstOrDefault(r => r.Key == f.Range) is { Key: not null } range)
        {
            var since = now - range.Span;
            q = q.Where(r => r.StartedAt >= since);
        }
        else
        {
            if (f.From is { } from)
            {
                var fromOffset = new DateTimeOffset(DateTime.SpecifyKind(from.Date, DateTimeKind.Utc));
                q = q.Where(r => r.StartedAt >= fromOffset);
            }
            if (f.To is { } to)
            {
                var toOffset = new DateTimeOffset(DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Utc));
                q = q.Where(r => r.StartedAt < toOffset);
            }
        }

        if (f.AppliedOnly)
        {
            q = q.Where(r => r.ActionsExecutedCount > 0);
        }
        if (f.FailuresOnly)
        {
            q = q.Where(IsFailure);
        }
        return q.ToList();
    }

    public static bool IsFailure(RunRecord r) =>
        r.Outcome is RunOutcome.Failed or RunOutcome.PartialFailure || r.ActionsFailedCount > 0;

    /// <summary>One page (1-based, clamped into range) of <paramref name="runs"/>, and the total count.</summary>
    public static (List<RunRecord> Items, int Total) Page(IReadOnlyList<RunRecord> runs, int page, int pageSize)
    {
        var pages = Math.Max(1, (int)Math.Ceiling(runs.Count / (double)pageSize));
        var p = Math.Clamp(page, 1, pages);
        return (runs.Skip((p - 1) * pageSize).Take(pageSize).ToList(), runs.Count);
    }

    /// <summary>
    /// Collapses consecutive runs of the same rule that changed nothing (0 applied, 0 failed, same
    /// outcome, no error) into one group, so a rule that runs every 5 minutes doesn't bury the rest.
    /// Anything eventful stays a group of one.
    /// </summary>
    public static List<HistoryGroup> Group(IReadOnlyList<RunRecord> runs)
    {
        var groups = new List<HistoryGroup>();
        var current = new List<RunRecord>();
        foreach (var run in runs)
        {
            if (current.Count > 0 && !(IsQuiet(run) && IsQuiet(current[0]) && run.RuleId == current[0].RuleId && run.Outcome == current[0].Outcome))
            {
                groups.Add(new HistoryGroup(current));
                current = [];
            }
            current.Add(run);
        }
        if (current.Count > 0)
        {
            groups.Add(new HistoryGroup(current));
        }
        return groups;
    }

    private static bool IsQuiet(RunRecord r) =>
        r.ActionsExecutedCount == 0 && r.ActionsFailedCount == 0 && r.ErrorMessage is null;
}

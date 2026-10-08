using TorrentRuler.Core.Domain;

namespace TorrentRuler.Web.Pages.Rules;

/// <summary>One row of the Rules list: the rule plus what the list shows about it.</summary>
/// <param name="ScheduleText">The cron expression in words.</param>
/// <param name="NextRun">When the scheduler will next run it (null: invalid schedule).</param>
/// <param name="LastRun">Its most recent run, if any.</param>
public sealed record RuleRow(Rule Rule, string ScheduleText, DateTimeOffset? NextRun, RunRecord? LastRun);

/// <summary>Sorting and filter chips for the Rules list, from the page's query string.</summary>
public static class RuleListQuery
{
    public static readonly IReadOnlyList<string> Sorts = ["priority", "name", "lastRun", "nextRun"];
    public static readonly IReadOnlyList<(string Key, string Label)> Filters =
        [("enabled", "Enabled"), ("disabled", "Disabled"), ("dryrun", "Dry run"), ("failed", "Failed last run")];

    /// <summary>
    /// Filters, then sorts. Unknown values fall back to no filter / priority order. Priority ties
    /// break by Id, the scheduler's own order; rows with no value for the sort key go last in both directions.
    /// </summary>
    public static IReadOnlyList<RuleRow> Apply(IEnumerable<RuleRow> rows, string? sort, bool desc, string? filter)
    {
        var filtered = filter switch
        {
            "enabled" => rows.Where(r => r.Rule.Enabled),
            "disabled" => rows.Where(r => !r.Rule.Enabled),
            "dryrun" => rows.Where(r => r.Rule.DryRun),
            "failed" => rows.Where(r => r.LastRun?.Outcome is RunOutcome.Failed or RunOutcome.PartialFailure),
            _ => rows
        };

        return sort switch
        {
            "name" => Order(filtered, r => r.Rule.Name, desc, StringComparer.OrdinalIgnoreCase),
            "lastRun" => NullsLast(filtered, r => r.LastRun?.StartedAt ?? r.Rule.LastRunAt, desc),
            "nextRun" => NullsLast(filtered, r => r.NextRun, desc),
            _ => Order(filtered, r => r.Rule.Priority, desc, Comparer<int>.Default)
        };
    }

    private static List<RuleRow> Order<TKey>(IEnumerable<RuleRow> rows, Func<RuleRow, TKey> key, bool desc, IComparer<TKey> comparer) =>
        (desc ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer)).ThenBy(r => r.Rule.Id).ToList();

    private static List<RuleRow> NullsLast(IEnumerable<RuleRow> rows, Func<RuleRow, DateTimeOffset?> key, bool desc)
    {
        var list = rows.ToList();
        var withValue = list.Where(r => key(r) is not null);
        var ordered = (desc ? withValue.OrderByDescending(r => key(r)) : withValue.OrderBy(r => key(r))).ThenBy(r => r.Rule.Id);
        return [.. ordered, .. list.Where(r => key(r) is null).OrderBy(r => r.Rule.Id)];
    }
}

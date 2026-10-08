using System.Text.Json;
using TorrentRuler.Core.Domain;
using TorrentRuler.Engine.Actions;

namespace TorrentRuler.Web.Pages;

/// <summary>One hour of run activity for the dashboard sparkline.</summary>
public sealed record HourBucket(DateTimeOffset Hour, int Matched, int Applied);

/// <summary>
/// The dashboard's last-24-hours figures. <see cref="MatchesTotal"/> sums every run's match count,
/// so a torrent matched by a rule that runs every 5 minutes counts 288 times; that is why
/// <see cref="UniqueTorrentsMatched"/> exists alongside it.
/// </summary>
public sealed record DashboardStats(
    int MatchesTotal,
    int UniqueTorrentsMatched,
    int AppliedTotal,
    IReadOnlyList<HourBucket> Hourly)
{
    public const int Hours = 24;

    /// <param name="runs">Runs to summarise; those started more than 24 hours before <paramref name="now"/> only count toward nothing.</param>
    public static DashboardStats Compute(IReadOnlyList<RunRecord> runs, DateTimeOffset now)
    {
        var currentHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
        var firstHour = currentHour.AddHours(-(Hours - 1));
        var matched = new int[Hours];
        var applied = new int[Hours];

        var unique = new HashSet<(int, string)>();
        var matchesTotal = 0;
        var appliedTotal = 0;

        foreach (var run in runs)
        {
            var started = run.StartedAt.ToOffset(now.Offset);
            var bucket = (int)Math.Floor((started - firstHour).TotalHours);
            if (bucket is < 0 or >= Hours)
            {
                continue;
            }

            matched[bucket] += run.MatchedCount;
            applied[bucket] += run.ActionsExecutedCount;
            matchesTotal += run.MatchedCount;
            appliedTotal += run.ActionsExecutedCount;

            // Run details record one ActionResult per (action, torrent); the torrents are the distinct pairs.
            foreach (var result in ReadDetails(run.DetailsJson))
            {
                unique.Add((result.InstanceId, result.TorrentHash));
            }
        }

        var hourly = Enumerable.Range(0, Hours)
            .Select(i => new HourBucket(firstHour.AddHours(i), matched[i], applied[i]))
            .ToList();
        return new DashboardStats(matchesTotal, unique.Count, appliedTotal, hourly);
    }

    private static List<ActionResult> ReadDetails(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<ActionResult>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

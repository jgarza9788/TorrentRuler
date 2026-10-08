using System.Text.Json;
using TorrentRuler.Core.Domain;
using TorrentRuler.Engine.Actions;
using TorrentRuler.Web.Pages;
using Xunit;

namespace TorrentRuler.Tests.Web;

public class DashboardStatsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 30, 0, TimeSpan.Zero);

    private static RunRecord Run(DateTimeOffset at, int matched, int applied, params (int Instance, string Hash)[] touched) => new()
    {
        StartedAt = at,
        MatchedCount = matched,
        ActionsExecutedCount = applied,
        DetailsJson = JsonSerializer.Serialize(touched.Select(t => new ActionResult
        {
            InstanceId = t.Instance, TorrentHash = t.Hash, ActionType = "AddTagsAction", Outcome = ActionOutcome.Applied
        }).ToList())
    };

    [Fact]
    public void Compute_CountsUniqueTorrentsAcrossRuns()
    {
        var stats = DashboardStats.Compute(
        [
            Run(Now.AddHours(-1), 2, 1, (1, "aaa"), (1, "bbb")),
            Run(Now.AddHours(-2), 2, 0, (1, "aaa"), (2, "aaa"))
        ], Now);

        Assert.Equal(4, stats.MatchesTotal);
        Assert.Equal(3, stats.UniqueTorrentsMatched); // (1,aaa), (1,bbb), (2,aaa)
        Assert.Equal(1, stats.AppliedTotal);
    }

    [Fact]
    public void Compute_SkipsMissingOrMalformedDetails()
    {
        var stats = DashboardStats.Compute(
        [
            new RunRecord { StartedAt = Now, MatchedCount = 3, DetailsJson = null },
            new RunRecord { StartedAt = Now, MatchedCount = 1, DetailsJson = "not json" }
        ], Now);

        Assert.Equal(4, stats.MatchesTotal);
        Assert.Equal(0, stats.UniqueTorrentsMatched);
    }

    [Fact]
    public void Compute_BucketsTheLast24HoursOldestFirst()
    {
        var stats = DashboardStats.Compute(
        [
            Run(Now.AddMinutes(-30), 5, 2),
            Run(Now.AddMinutes(-20), 1, 1),
            Run(Now.AddHours(-23).AddMinutes(-10), 7, 0),
            Run(Now.AddHours(-30), 100, 100) // outside the window
        ], Now);

        Assert.Equal(24, stats.Hourly.Count);
        Assert.True(stats.Hourly.Zip(stats.Hourly.Skip(1)).All(p => p.First.Hour < p.Second.Hour));
        Assert.Equal((6, 3), (stats.Hourly[^1].Matched, stats.Hourly[^1].Applied));
        Assert.Equal((7, 0), (stats.Hourly[0].Matched, stats.Hourly[0].Applied));
        Assert.Equal(13, stats.Hourly.Sum(h => h.Matched));
    }
}

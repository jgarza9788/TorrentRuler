using TorrentRuler.Core.Domain;
using TorrentRuler.Web.Pages.History;
using Xunit;

namespace TorrentRuler.Tests.Web;

public class HistoryQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static long _id = 1000;

    private static RunRecord Run(int ruleId, TimeSpan ago, RunOutcome outcome = RunOutcome.Success, int applied = 0, int failed = 0, string? error = null) => new()
    {
        Id = _id--, RuleId = ruleId, StartedAt = Now - ago, FinishedAt = Now - ago + TimeSpan.FromSeconds(1),
        Outcome = outcome, ActionsExecutedCount = applied, ActionsFailedCount = failed, ErrorMessage = error
    };

    [Fact]
    public void Range1h_ExcludesOlderRuns()
    {
        var runs = new[] { Run(1, TimeSpan.FromMinutes(10)), Run(1, TimeSpan.FromHours(2)) };

        var result = HistoryQuery.Apply(runs, new HistoryFilter(Range: "1h"), Now);

        Assert.Single(result);
    }

    [Fact]
    public void Range_OverridesFromAndTo()
    {
        var runs = new[] { Run(1, TimeSpan.FromHours(30)), Run(1, TimeSpan.FromHours(1)) };

        var result = HistoryQuery.Apply(runs, new HistoryFilter(From: Now.AddDays(-10).DateTime, To: Now.AddDays(-5).DateTime, Range: "24h"), Now);

        Assert.Single(result);
    }

    [Fact]
    public void AppliedOnly_KeepsRunsThatChangedSomething()
    {
        var runs = new[] { Run(1, TimeSpan.FromMinutes(1), applied: 2), Run(1, TimeSpan.FromMinutes(2)) };

        Assert.Single(HistoryQuery.Apply(runs, new HistoryFilter(AppliedOnly: true), Now));
    }

    [Fact]
    public void FailuresOnly_KeepsFailedPartialAndActionFailures()
    {
        var runs = new[]
        {
            Run(1, TimeSpan.FromMinutes(1), RunOutcome.Failed),
            Run(1, TimeSpan.FromMinutes(2), RunOutcome.PartialFailure),
            Run(1, TimeSpan.FromMinutes(3), RunOutcome.Success, failed: 1),
            Run(1, TimeSpan.FromMinutes(4), RunOutcome.Success)
        };

        Assert.Equal(3, HistoryQuery.Apply(runs, new HistoryFilter(FailuresOnly: true), Now).Count);
    }

    [Fact]
    public void Page_ReturnsTheRequestedSliceAndTheTotal()
    {
        var runs = Enumerable.Range(0, 120).Select(i => Run(1, TimeSpan.FromMinutes(i))).ToList();

        var (items, total) = HistoryQuery.Page(runs, page: 2, pageSize: 50);

        Assert.Equal(120, total);
        Assert.Equal(runs.Skip(50).Take(50), items);
    }

    [Fact]
    public void Page_ClampsOutOfRangePages()
    {
        var runs = Enumerable.Range(0, 10).Select(i => Run(1, TimeSpan.FromMinutes(i))).ToList();

        Assert.Equal(runs, HistoryQuery.Page(runs, page: 99, pageSize: 50).Items);
        Assert.Equal(runs, HistoryQuery.Page(runs, page: 0, pageSize: 50).Items);
    }

    [Fact]
    public void Group_CollapsesConsecutiveIdenticalQuietRuns()
    {
        var runs = new[]
        {
            Run(1, TimeSpan.FromMinutes(1)),
            Run(1, TimeSpan.FromMinutes(2)),
            Run(1, TimeSpan.FromMinutes(3)),
            Run(2, TimeSpan.FromMinutes(4)),                 // different rule
            Run(1, TimeSpan.FromMinutes(5), applied: 1),     // did something
            Run(1, TimeSpan.FromMinutes(6)),
            Run(1, TimeSpan.FromMinutes(7), RunOutcome.Failed, error: "boom")
        };

        var groups = HistoryQuery.Group(runs);

        Assert.Equal([3, 1, 1, 1, 1], groups.Select(g => g.Runs.Count));
        Assert.Same(runs[0], groups[0].Runs[0]);
    }
}

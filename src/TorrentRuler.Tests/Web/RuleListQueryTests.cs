using TorrentRuler.Core.Domain;
using TorrentRuler.Web.Pages.Rules;
using Xunit;

namespace TorrentRuler.Tests.Web;

public class RuleListQueryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private static RuleRow Row(int id, string name, int priority, DateTimeOffset? next = null, DateTimeOffset? lastRun = null,
        bool enabled = true, bool dryRun = false, RunOutcome? lastOutcome = null) => new(
        new Rule
        {
            Id = id, Name = name, Priority = priority, Enabled = enabled, DryRun = dryRun, LastRunAt = lastRun,
            CronExpression = "*/5 * * * *", ConditionTreeJson = "{}", ActionsJson = "[]", TargetInstanceIdsJson = "[]"
        },
        "every 5 minutes",
        next,
        lastOutcome is { } o ? new RunRecord { RuleId = id, Outcome = o, StartedAt = lastRun ?? T0 } : null);

    private static readonly RuleRow[] Rows =
    [
        Row(1, "Bravo", 2, next: T0.AddMinutes(5), lastRun: T0.AddHours(-1), lastOutcome: RunOutcome.Success),
        Row(2, "alpha", 1, next: null, enabled: false),
        Row(3, "Charlie", 1, next: T0.AddMinutes(1), lastRun: T0.AddHours(-2), dryRun: true, lastOutcome: RunOutcome.Failed),
        Row(4, "Delta", 3, next: T0.AddMinutes(9), lastRun: T0.AddMinutes(-5), lastOutcome: RunOutcome.PartialFailure)
    ];

    private static int[] Ids(IEnumerable<RuleRow> rows) => [.. rows.Select(r => r.Rule.Id)];

    [Fact]
    public void Default_IsPriorityThenId() => Assert.Equal([2, 3, 1, 4], Ids(RuleListQuery.Apply(Rows, null, false, null)));

    [Fact]
    public void Name_IsCaseInsensitive() => Assert.Equal([2, 1, 3, 4], Ids(RuleListQuery.Apply(Rows, "name", false, null)));

    [Fact]
    public void NextRun_PutsNullsLast_InBothDirections()
    {
        Assert.Equal([3, 1, 4, 2], Ids(RuleListQuery.Apply(Rows, "nextRun", false, null)));
        Assert.Equal([4, 1, 3, 2], Ids(RuleListQuery.Apply(Rows, "nextRun", true, null)));
    }

    [Fact]
    public void LastRun_Descending_MostRecentFirst_NeverRunLast() =>
        Assert.Equal([4, 1, 3, 2], Ids(RuleListQuery.Apply(Rows, "lastRun", true, null)));

    [Theory]
    [InlineData("enabled", new[] { 3, 1, 4 })]
    [InlineData("disabled", new[] { 2 })]
    [InlineData("dryrun", new[] { 3 })]
    [InlineData("failed", new[] { 3, 4 })]
    public void Filters(string filter, int[] expected) => Assert.Equal(expected, Ids(RuleListQuery.Apply(Rows, null, false, filter)));

    [Fact]
    public void UnknownSortOrFilter_FallsBackToDefaults() =>
        Assert.Equal([2, 3, 1, 4], Ids(RuleListQuery.Apply(Rows, "bogus", false, "bogus")));
}

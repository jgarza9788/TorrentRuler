using TorrentRuler.Core.Domain;
using TorrentRuler.Engine.Scheduling;
using Xunit;

namespace TorrentRuler.Tests.Engine;

public class RuleSchedulerServiceTests
{
    private static Rule MakeRule(string cron, DateTimeOffset? lastRunAt) => new()
    {
        Name = "test-rule",
        CronExpression = cron,
        TimeZoneId = "UTC",
        ConditionTreeJson = "{}",
        ActionsJson = "[]",
        TargetInstanceIdsJson = "[]",
        LastRunAt = lastRunAt
    };

    [Fact]
    public void NextRunAt_IsTheNextOccurrenceAfterTheLastRun()
    {
        var rule = MakeRule("*/5 * * * *", DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:05:00Z"), RuleSchedulerService.NextRunAt(rule, DateTimeOffset.Parse("2026-01-01T00:03:00Z")));
    }

    [Fact]
    public void NextRunAt_Overdue_IsNow()
    {
        // The scheduler picks an overdue rule up on its next tick, so "now" is the honest answer.
        var rule = MakeRule("*/5 * * * *", DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var now = DateTimeOffset.Parse("2026-01-01T00:12:00Z");

        Assert.Equal(now, RuleSchedulerService.NextRunAt(rule, now));
    }

    [Fact]
    public void NextRunAt_InvalidCron_IsNull() =>
        Assert.Null(RuleSchedulerService.NextRunAt(MakeRule("not a cron", null), DateTimeOffset.UtcNow));

    [Fact]
    public void IsDue_True_WhenNextOccurrenceHasArrived()
    {
        var rule = MakeRule("*/5 * * * *", DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var now = DateTimeOffset.Parse("2026-01-01T00:05:00Z");

        Assert.True(RuleSchedulerService.IsDue(rule, now));
    }

    [Fact]
    public void IsDue_False_BeforeNextOccurrence()
    {
        var rule = MakeRule("*/5 * * * *", DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var now = DateTimeOffset.Parse("2026-01-01T00:03:00Z");

        Assert.False(RuleSchedulerService.IsDue(rule, now));
    }

    [Fact]
    public void IsDue_HandlesNeverRunBefore_UsingOneMinuteLookback()
    {
        var rule = MakeRule("*/5 * * * *", lastRunAt: null);
        var now = DateTimeOffset.Parse("2026-01-01T00:05:00Z");

        Assert.True(RuleSchedulerService.IsDue(rule, now));
    }

    [Fact]
    public void IsDue_False_ForInvalidCronExpression()
    {
        // */1 violates the 5-minute minimum -- CronValidator rejects it, so it can never be due.
        var rule = MakeRule("*/1 * * * *", DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var now = DateTimeOffset.Parse("2026-01-01T01:00:00Z");

        Assert.False(RuleSchedulerService.IsDue(rule, now));
    }

    [Fact]
    public void IsDue_False_JustAfterLastRun()
    {
        var rule = MakeRule("0 3 * * *", DateTimeOffset.Parse("2026-01-01T03:00:00Z"));
        var now = DateTimeOffset.Parse("2026-01-01T03:01:00Z");

        Assert.False(RuleSchedulerService.IsDue(rule, now));
    }
}

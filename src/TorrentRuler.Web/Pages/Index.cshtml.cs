using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TorrentRuler.Core.Domain;
using TorrentRuler.Engine.Scheduling;
using TorrentRuler.Infrastructure.Persistence;

namespace TorrentRuler.Web.Pages;

public class IndexModel(AppDbContext db) : PageModel
{
    public sealed record UpcomingRun(int RuleId, string RuleName, DateTimeOffset At, bool DryRun);

    public const int UpcomingCount = 5;

    public AppSettings Settings { get; private set; } = null!;
    public int EnabledInstanceCount { get; private set; }
    public int TotalInstanceCount { get; private set; }
    public int EnabledRuleCount { get; private set; }
    public int TotalRuleCount { get; private set; }
    public List<Instance> Instances { get; private set; } = [];
    public List<RunRecord> RecentRuns { get; private set; } = [];
    public Dictionary<int, string> RuleNamesById { get; private set; } = [];
    public DashboardStats Stats { get; private set; } = null!;
    public int RunsLast24h { get; private set; }
    public int FailedLast24h { get; private set; }
    public List<UpcomingRun> Upcoming { get; private set; } = [];
    public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Settings = await db.AppSettings.AsNoTracking().SingleAsync(s => s.Id == 1, ct);

        Instances = await db.Instances.AsNoTracking().OrderBy(i => i.Name).ToListAsync(ct);
        EnabledInstanceCount = Instances.Count(i => i.Enabled);
        TotalInstanceCount = Instances.Count;

        var rules = await db.Rules.AsNoTracking().ToListAsync(ct);
        EnabledRuleCount = rules.Count(r => r.Enabled);
        TotalRuleCount = rules.Count;
        RuleNamesById = rules.ToDictionary(r => r.Id, r => r.Name);

        Upcoming = rules
            .Where(r => r.Enabled)
            .Select(r => RuleSchedulerService.NextRunAt(r, Now) is { } at ? new UpcomingRun(r.Id, r.Name, at, r.DryRun) : null)
            .OfType<UpcomingRun>()
            .OrderBy(u => u.At)
            .ThenBy(u => u.RuleName)
            .Take(UpcomingCount)
            .ToList();

        // SQLite's EF Core provider can't translate ORDER BY on DateTimeOffset columns;
        // Id is an auto-increment PK assigned in real-time insertion order, so ordering
        // by it descending is equivalent here and does translate.
        RecentRuns = await db.RunRecords.AsNoTracking()
            .OrderByDescending(r => r.Id)
            .Take(10)
            .ToListAsync(ct);

        // EF Core's SQLite provider can't translate DateTimeOffset comparisons in WHERE
        // either -- fetch a bounded recent set by Id and filter the time window client-side.
        var since = Now.AddHours(-DashboardStats.Hours);
        var recentWindow = (await db.RunRecords.AsNoTracking()
                .OrderByDescending(r => r.Id)
                .Take(5000)
                .ToListAsync(ct))
            .Where(r => r.StartedAt >= since)
            .ToList();
        Stats = DashboardStats.Compute(recentWindow, Now);
        RunsLast24h = recentWindow.Count;
        FailedLast24h = recentWindow.Count(r => r.Outcome == RunOutcome.Failed || r.Outcome == RunOutcome.PartialFailure);
    }
}

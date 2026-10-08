using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TorrentRuler.Core.Domain;
using TorrentRuler.Engine;
using TorrentRuler.Engine.Scheduling;
using TorrentRuler.Infrastructure.Persistence;

namespace TorrentRuler.Web.Pages.Rules;

public class IndexModel(AppDbContext db, IRuleRunner ruleRunner) : PageModel
{
    /// <summary>TempData key holding the most recently deleted rule (JSON), for Undo.</summary>
    public const string UndoKey = "qf.undo.rule";

    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true)] public bool Desc { get; set; }
    [BindProperty(SupportsGet = true)] public string? Filter { get; set; }

    public IReadOnlyList<RuleRow> Rows { get; private set; } = [];
    public int TotalCount { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var rules = await db.Rules.AsNoTracking().ToListAsync(ct);
        TotalCount = rules.Count;

        var lastRunIds = await db.RunRecords.AsNoTracking()
            .GroupBy(r => r.RuleId)
            .Select(g => g.Max(r => r.Id))
            .ToListAsync(ct);
        var lastRuns = await db.RunRecords.AsNoTracking()
            .Where(r => lastRunIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.RuleId, ct);

        var now = DateTimeOffset.UtcNow;
        var rows = rules.Select(r => new RuleRow(
            r,
            CronDescriptionService.Describe(r.CronExpression),
            RuleSchedulerService.NextRunAt(r, now),
            lastRuns.GetValueOrDefault(r.Id)));
        Rows = RuleListQuery.Apply(rows, Sort, Desc, Filter);
    }

    /// <summary>Bulk actions on the checked rules: enable | disable | run | delete.</summary>
    public async Task<IActionResult> OnPostBulkAsync(string op, int[] ids, CancellationToken ct)
    {
        var rules = await db.Rules.Where(r => ids.Contains(r.Id)).OrderBy(r => r.Priority).ThenBy(r => r.Id).ToListAsync(ct);
        if (rules.Count == 0)
        {
            Toasts.Add(TempData, ToastKind.Info, "No rules selected.");
            return RedirectToPage();
        }

        var now = DateTimeOffset.UtcNow;
        switch (op)
        {
            case "enable" or "disable":
                foreach (var rule in rules)
                {
                    rule.Enabled = op == "enable";
                    rule.UpdatedAt = now;
                }
                await db.SaveChangesAsync(ct);
                Toasts.Add(TempData, ToastKind.Success, $"{(op == "enable" ? "Enabled" : "Disabled")} {rules.Count} rule(s).");
                break;
            case "run":
                foreach (var rule in rules)
                {
                    await ruleRunner.RunAsync(rule.Id, ct);
                }
                Toasts.Add(TempData, ToastKind.Success, $"Ran {rules.Count} rule(s). See History for the results.");
                break;
            case "delete":
                db.Rules.RemoveRange(rules);
                await db.SaveChangesAsync(ct);
                Toasts.Add(TempData, ToastKind.Success, $"Deleted {rules.Count} rule(s).");
                break;
            default:
                Toasts.Add(TempData, ToastKind.Error, $"Unknown bulk action \"{op}\".");
                break;
        }
        return RedirectToPage(new { Sort, Desc, Filter });
    }

    /// <summary>HTMX: the inline priority box. 204 -- the box already shows the new value.</summary>
    public async Task<IActionResult> OnPostSetPriorityAsync(int id, int priority, CancellationToken ct)
    {
        var rule = await db.Rules.FindAsync([id], ct);
        if (rule is null)
        {
            return NotFound();
        }

        rule.Priority = priority;
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        Toasts.Trigger(Response, ToastKind.Success, $"\"{rule.Name}\" priority set to {priority}.");
        return new NoContentResult();
    }

    public async Task<IActionResult> OnPostToggleEnabledAsync(int id, CancellationToken ct)
    {
        var rule = await db.Rules.FindAsync([id], ct);
        if (rule is not null)
        {
            rule.Enabled = !rule.Enabled;
            rule.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            Toasts.Add(TempData, ToastKind.Success, $"{(rule.Enabled ? "Enabled" : "Disabled")} \"{rule.Name}\".");
        }
        return RedirectToPage(new { Sort, Desc, Filter });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        var rule = await db.Rules.FindAsync([id], ct);
        if (rule is not null)
        {
            db.Rules.Remove(rule);
            await db.SaveChangesAsync(ct);
            // Only the latest delete can be undone; it overwrites any earlier one.
            TempData[UndoKey] = JsonSerializer.Serialize(rule);
            Toasts.Add(TempData, ToastKind.Success, $"Deleted rule \"{rule.Name}\".", undoUrl: "/Rules?handler=RestoreDeleted");
        }
        return RedirectToPage();
    }

    /// <summary>
    /// Undo for the most recent delete. Restores under the original Id where it's still free, so the
    /// rule's run history (RunRecords keep their RuleId) links up again.
    /// </summary>
    public async Task<IActionResult> OnPostRestoreDeletedAsync(CancellationToken ct)
    {
        // Removed explicitly (not just read) so an undo can only ever happen once.
        var json = TempData[UndoKey] as string;
        TempData.Remove(UndoKey);
        if (json is null || JsonSerializer.Deserialize<Rule>(json) is not { } rule)
        {
            Toasts.Add(TempData, ToastKind.Info, "Nothing to undo.");
            return RedirectToPage();
        }

        if (await db.Rules.AnyAsync(r => r.Name == rule.Name, ct))
        {
            Toasts.Add(TempData, ToastKind.Error, $"Can't restore \"{rule.Name}\": another rule now has that name.");
            return RedirectToPage();
        }
        if (await db.Rules.AnyAsync(r => r.Id == rule.Id, ct))
        {
            rule.Id = 0;
        }

        rule.UpdatedAt = DateTimeOffset.UtcNow;
        db.Rules.Add(rule);
        await db.SaveChangesAsync(ct);
        Toasts.Add(TempData, ToastKind.Success, $"Restored rule \"{rule.Name}\".");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDuplicateAsync(int id, CancellationToken ct)
    {
        var rule = await db.Rules.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null)
        {
            return RedirectToPage();
        }

        var now = DateTimeOffset.UtcNow;
        var copy = new Rule
        {
            Name = await NextCopyNameAsync(rule.Name, ct),
            Description = rule.Description,
            // A copy starts disabled so it cannot fire on the original's schedule before
            // the user has had a chance to adjust it.
            Enabled = false,
            Priority = rule.Priority,
            StopOnMatch = rule.StopOnMatch,
            DryRun = rule.DryRun,
            CronExpression = rule.CronExpression,
            TimeZoneId = rule.TimeZoneId,
            ConditionTreeJson = rule.ConditionTreeJson,
            AdvancedSql = rule.AdvancedSql,
            UseAdvancedSql = rule.UseAdvancedSql,
            ActionsJson = rule.ActionsJson,
            TargetInstanceIdsJson = rule.TargetInstanceIdsJson,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Rules.Add(copy);
        await db.SaveChangesAsync(ct);
        Toasts.Add(TempData, ToastKind.Success, $"Created \"{copy.Name}\" (disabled until you enable it).");
        return RedirectToPage("/Rules/Edit", new { id = copy.Id });
    }

    /// <summary>"Foo" -> "Foo (copy)", then "Foo (copy 2)", "Foo (copy 3)", ... for further copies.</summary>
    private async Task<string> NextCopyNameAsync(string sourceName, CancellationToken ct)
    {
        var baseName = Regex.Replace(sourceName, @"\s*\(copy(?: \d+)?\)$", "");
        var taken = await db.Rules.AsNoTracking()
            .Where(r => r.Name.StartsWith(baseName))
            .Select(r => r.Name)
            .ToListAsync(ct);

        var candidate = $"{baseName} (copy)";
        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{baseName} (copy {n})";
        }

        return candidate;
    }

    public async Task<IActionResult> OnPostRunNowAsync(int id, CancellationToken ct)
    {
        await ruleRunner.RunAsync(id, ct);
        var run = await db.RunRecords.AsNoTracking().Where(r => r.RuleId == id).OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
        var name = await db.Rules.AsNoTracking().Where(r => r.Id == id).Select(r => r.Name).SingleOrDefaultAsync(ct) ?? "Rule";
        if (run is null)
        {
            Toasts.Add(TempData, ToastKind.Info, $"\"{name}\" did not run.");
        }
        else if (run.Outcome is RunOutcome.Failed)
        {
            Toasts.Add(TempData, ToastKind.Error, $"\"{name}\" failed: {run.ErrorMessage}");
        }
        else
        {
            Toasts.Add(TempData, run.ActionsFailedCount > 0 ? ToastKind.Error : ToastKind.Success,
                $"\"{name}\" ran: {run.MatchedCount} matched, {run.ActionsExecutedCount} applied, {run.ActionsFailedCount} failed.");
        }
        return RedirectToPage(new { Sort, Desc, Filter });
    }
}

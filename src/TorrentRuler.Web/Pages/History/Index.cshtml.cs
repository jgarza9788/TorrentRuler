using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TorrentRuler.Core.Domain;
using ActionResult = TorrentRuler.Engine.Actions.ActionResult;
using TorrentRuler.Infrastructure.Persistence;

namespace TorrentRuler.Web.Pages.History;

public class IndexModel(AppDbContext db) : PageModel
{
    public const int PageSize = 50;

    /// <summary>How far back the page looks. Older runs are still in the database, just not listed.</summary>
    public const int Window = 5000;

    [BindProperty(SupportsGet = true)] public int? RuleId { get; set; }
    [BindProperty(SupportsGet = true)] public RunOutcome? Outcome { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Range { get; set; }
    [BindProperty(SupportsGet = true)] public bool AppliedOnly { get; set; }
    [BindProperty(SupportsGet = true)] public bool FailuresOnly { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;

    public List<HistoryGroup> Groups { get; private set; } = [];
    public Dictionary<int, string> RuleNamesById { get; private set; } = [];
    public List<Rule> AllRules { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int FirstIndex { get; private set; }
    public int LastIndex { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public double MaxDurationMs { get; private set; }

    public bool HasFilters => RuleId is not null || Outcome is not null || From is not null || To is not null
        || Range is not null || AppliedOnly || FailuresOnly;

    public async Task OnGetAsync(CancellationToken ct)
    {
        AllRules = await db.Rules.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        RuleNamesById = AllRules.ToDictionary(r => r.Id, r => r.Name);

        // SQLite's EF Core provider can't translate DateTimeOffset comparisons (WHERE or ORDER BY):
        // order by Id (insertion order, equivalent to StartedAt here), take a bounded window, and
        // let HistoryQuery do the rest in memory.
        var candidates = await db.RunRecords.AsNoTracking().OrderByDescending(r => r.Id).Take(Window).ToListAsync(ct);

        var filtered = HistoryQuery.Apply(candidates,
            new HistoryFilter(RuleId, Outcome, From, To, Range, AppliedOnly, FailuresOnly), DateTimeOffset.UtcNow);
        var (page, total) = HistoryQuery.Page(filtered, PageNumber, PageSize);

        TotalCount = total;
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        FirstIndex = total == 0 ? 0 : (PageNumber - 1) * PageSize + 1;
        LastIndex = FirstIndex + page.Count - (total == 0 ? 0 : 1);
        Groups = HistoryQuery.Group(page);
        MaxDurationMs = page.Select(DurationMs).DefaultIfEmpty(0).Max();
    }

    public static double DurationMs(RunRecord run) =>
        run.FinishedAt is { } end ? Math.Max(0, (end - run.StartedAt).TotalMilliseconds) : 0;

    /// <summary>The route values for this page with one setting changed -- for filter chips, ranges and the pager.</summary>
    public Dictionary<string, string?> Route(string? key = null, object? value = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["RuleId"] = RuleId?.ToString(),
            ["Outcome"] = Outcome?.ToString(),
            ["From"] = From?.ToString("yyyy-MM-dd"),
            ["To"] = To?.ToString("yyyy-MM-dd"),
            ["Range"] = Range,
            ["AppliedOnly"] = AppliedOnly ? "true" : null,
            ["FailuresOnly"] = FailuresOnly ? "true" : null
        };
        if (key is not null)
        {
            values[key] = value switch { null => null, bool b => b ? "true" : null, _ => value.ToString() };
        }
        return values.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public static List<ActionResult> ParseDetails(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ActionResult>>(detailsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The run's details as indented JSON for the raw view (as stored, if it isn't valid JSON).</summary>
    public static string PrettyDetails(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return "(no details recorded)";
        }
        try
        {
            using var doc = JsonDocument.Parse(detailsJson);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return detailsJson;
        }
    }
}

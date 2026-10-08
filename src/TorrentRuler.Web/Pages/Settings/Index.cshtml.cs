using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TorrentRuler.Core.Domain;
using TorrentRuler.Infrastructure.Config;
using TorrentRuler.Infrastructure.Persistence;
using TorrentRuler.Web.Diagnostics;
using TorrentRuler.Web.Logging;

namespace TorrentRuler.Web.Pages.Settings;

public class IndexModel(AppDbContext db, IConfigPortabilityService configService, IHostEnvironment hostEnvironment) : PageModel
{
    [BindProperty]
    public SettingsInput Input { get; set; } = new();

    public List<PathMappingRule> PathMappingRules { get; private set; } = [];

    public string? ImportError { get; set; }
    public bool SettingsSaved { get; set; }

    /// <summary>Set after "Preview import": what the file would change, plus what's needed to apply it.</summary>
    public ImportPreview? Preview { get; private set; }
    public string? PreviewContent { get; private set; }
    public ConfigFormat PreviewFormat { get; private set; }
    public string PreviewKind { get; private set; } = "config";

    /// <summary>Each parallelism level with its concurrent-requests-per-host figure, from the mapping the sources use.</summary>
    public static IEnumerable<(ParallelismLevel Level, string Label)> ParallelismOptions =>
        Enum.GetValues<ParallelismLevel>().Select(l => (l,
            $"{(l == ParallelismLevel.VeryHigh ? "Very high" : l.ToString())} ({TorrentRuler.Sources.Concurrency.ParallelismMapping.WorkerCount(l)} requests per host)"));

    /// <summary>Plain-text summary for "Copy diagnostics" (the browser adds its user agent).</summary>
    public string Diagnostics { get; private set; } = "";

    public string GitBranch => BuildInfo.GitBranch;
    public string GitCommit => BuildInfo.GitCommit;
    public DateTime? BuildTimeUtc => BuildInfo.BuildTimeUtc;
    public string RuntimeVersion => BuildInfo.RuntimeVersion;
    public string EnvironmentName => hostEnvironment.EnvironmentName;

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var settings = await db.AppSettings.AsNoTracking().SingleAsync(s => s.Id == 1, ct);
        Input = new SettingsInput
        {
            ParallelismLevel = settings.ParallelismLevel,
            GlobalDryRun = settings.GlobalDryRun,
            GlobalKillSwitch = settings.GlobalKillSwitch,
            Theme = settings.Theme,
            LogLevel = settings.LogLevel,
            TimeZoneId = settings.TimeZoneId
        };
        PathMappingRules = await db.PathMappingRules.AsNoTracking().OrderBy(r => r.Id).ToListAsync(ct);

        var instances = await db.Instances.AsNoTracking().CountAsync(ct);
        var enabledInstances = await db.Instances.AsNoTracking().CountAsync(i => i.Enabled, ct);
        var rules = await db.Rules.AsNoTracking().CountAsync(ct);
        var enabledRules = await db.Rules.AsNoTracking().CountAsync(r => r.Enabled, ct);
        var storage = await db.StoragePaths.AsNoTracking().CountAsync(ct);
        Diagnostics = string.Join("\n",
            "torrentruler diagnostics",
            $"Branch: {GitBranch}",
            $"Commit: {GitCommit}",
            $"Built (UTC): {BuildTimeUtc?.ToString("u") ?? "unknown"}",
            $".NET runtime: {RuntimeVersion}",
            $"Environment: {EnvironmentName}",
            $"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}",
            $"Instances: {enabledInstances} of {instances} enabled",
            $"Rules: {enabledRules} of {rules} enabled",
            $"Storage paths: {storage}",
            $"Path mappings: {PathMappingRules.Count}",
            $"Parallelism: {settings.ParallelismLevel}, log level: {settings.LogLevel}, time zone: {settings.TimeZoneId}",
            $"Global dry run: {(settings.GlobalDryRun ? "ON" : "off")}, kill switch: {(settings.GlobalKillSwitch ? "ON" : "off")}");
    }

    public async Task<IActionResult> OnPostSaveSettingsAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(ct);
            return Page();
        }

        var settings = await db.AppSettings.SingleAsync(s => s.Id == 1, ct);
        settings.ParallelismLevel = Input.ParallelismLevel;
        settings.GlobalDryRun = Input.GlobalDryRun;
        settings.GlobalKillSwitch = Input.GlobalKillSwitch;
        settings.Theme = Input.Theme;
        settings.LogLevel = Input.LogLevel;
        settings.TimeZoneId = Input.TimeZoneId;
        await db.SaveChangesAsync(ct);

        // Apply the new log level to the running NLog config immediately (no restart).
        NLogSetup.ApplyMinLevel(NLogSetup.MapLevel(settings.LogLevel));

        SettingsSaved = true;
        Toasts.Add(TempData, ToastKind.Success, "Settings saved.");
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostTogglePathMappingAsync(int id, CancellationToken ct)
    {
        var rule = await db.PathMappingRules.FindAsync([id], ct);
        if (rule is not null)
        {
            rule.Enabled = !rule.Enabled;
            await db.SaveChangesAsync(ct);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeletePathMappingAsync(int id, CancellationToken ct)
    {
        var rule = await db.PathMappingRules.FindAsync([id], ct);
        if (rule is not null)
        {
            db.PathMappingRules.Remove(rule);
            await db.SaveChangesAsync(ct);
            Toasts.Add(TempData, ToastKind.Success, $"Deleted path mapping \"{rule.SourcePrefix}\".");
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetExportConfigAsync(ConfigFormat format, CancellationToken ct)
    {
        var content = await configService.ExportConfigAsync(format, ct);
        var ext = format == ConfigFormat.Json ? "json" : "yaml";
        return File(Encoding.UTF8.GetBytes(content), "application/octet-stream", $"torrentruler-config.{ext}");
    }

    public async Task<IActionResult> OnGetExportRulesAsync(ConfigFormat format, CancellationToken ct)
    {
        var content = await configService.ExportRulesAsync(format, ct);
        var ext = format == ConfigFormat.Json ? "json" : "yaml";
        return File(Encoding.UTF8.GetBytes(content), "application/octet-stream", $"torrentruler-rules.{ext}");
    }

    /// <summary>Step 1 of an import: read the uploaded file and show what it would change. Writes nothing.</summary>
    public async Task<IActionResult> OnPostPreviewImportAsync(IFormFile? importFile, ConfigFormat importFormat, string importKind, CancellationToken ct)
    {
        await LoadAsync(ct);
        if (importFile is null || importFile.Length == 0)
        {
            ImportError = "Choose a file to import.";
            return Page();
        }

        using var reader = new StreamReader(importFile.OpenReadStream());
        PreviewContent = await reader.ReadToEndAsync(ct);
        PreviewFormat = importFormat;
        PreviewKind = importKind == "rules" ? "rules" : "config";
        Preview = await configService.PreviewImportAsync(PreviewContent, PreviewFormat, PreviewKind, ct);
        return Page();
    }

    /// <summary>
    /// Step 2: apply. Takes the previewed text back from the page (a hidden field, so no server-side
    /// state), or a file posted directly.
    /// </summary>
    public async Task<IActionResult> OnPostImportConfigAsync(IFormFile? importFile, string? importContent, ConfigFormat importFormat, string importKind, CancellationToken ct)
    {
        var content = importContent;
        if (string.IsNullOrEmpty(content) && importFile is { Length: > 0 })
        {
            using var reader = new StreamReader(importFile.OpenReadStream());
            content = await reader.ReadToEndAsync(ct);
        }
        if (string.IsNullOrEmpty(content))
        {
            Toasts.Add(TempData, ToastKind.Error, "Choose a file to import.");
            return RedirectToPage();
        }

        try
        {
            if (importKind == "rules")
            {
                await configService.ImportRulesAsync(content, importFormat, ct);
                Toasts.Add(TempData, ToastKind.Success, "Rules imported.");
            }
            else
            {
                await configService.ImportConfigAsync(content, importFormat, ct);
                Toasts.Add(TempData, ToastKind.Success, "Config imported. Re-enter credentials for any new instances.");
            }
        }
        catch (Exception ex)
        {
            Toasts.Add(TempData, ToastKind.Error, $"Import failed: {ex.Message}");
        }
        return RedirectToPage();
    }

    public class SettingsInput
    {
        [System.ComponentModel.DataAnnotations.Display(Name = "Parallelism level")]
        public ParallelismLevel ParallelismLevel { get; set; } = ParallelismLevel.Medium;

        [System.ComponentModel.DataAnnotations.Display(Name = "Global dry-run (preview only, apply no actions anywhere)")]
        public bool GlobalDryRun { get; set; }

        [System.ComponentModel.DataAnnotations.Display(Name = "Global kill switch (stop all rules from running)")]
        public bool GlobalKillSwitch { get; set; }

        public string Theme { get; set; } = "system";

        [System.ComponentModel.DataAnnotations.Display(Name = "Log level")]
        public string LogLevel { get; set; } = "Information";

        [System.ComponentModel.DataAnnotations.Display(Name = "Timezone")]
        public string TimeZoneId { get; set; } = "UTC";
    }
}

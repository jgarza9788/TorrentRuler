using Microsoft.EntityFrameworkCore;
using TorrentRuler.Infrastructure.Persistence;

namespace TorrentRuler.Infrastructure.Config;

public enum ImportChangeKind { Add, Update, Unchanged }

/// <summary>What importing one item would do. <c>Section</c> is Rule | Instance | StoragePath | Settings.</summary>
public sealed record ImportChange(string Section, string Name, ImportChangeKind Kind, IReadOnlyList<string> ChangedFields);

/// <summary>A dry run of an import: every item it would add, update or leave alone -- or why the file can't be read.</summary>
public sealed record ImportPreview(IReadOnlyList<ImportChange> Changes, string? Error)
{
    public static ImportPreview Failure(string error) => new([], error);
    public bool HasChanges => Changes.Any(c => c.Kind != ImportChangeKind.Unchanged);
}

public partial class ConfigPortabilityService
{
    /// <summary>
    /// Compares a file with the database the way <see cref="ImportConfigAsync"/> / <see cref="ImportRulesAsync"/>
    /// would apply it (upsert by Name), without writing anything.
    /// </summary>
    /// <param name="kind">"config" (instances, storage paths, settings) or "rules".</param>
    public async Task<ImportPreview> PreviewImportAsync(string content, ConfigFormat format, string kind, CancellationToken ct = default)
    {
        try
        {
            return kind == "rules"
                ? await PreviewRulesAsync(ConfigSerializer.Deserialize<RulesExportDto>(content, format), ct)
                : await PreviewConfigAsync(ConfigSerializer.Deserialize<ConfigExportDto>(content, format), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Malformed JSON/YAML, missing required fields, unknown enum values: report, don't throw.
            return ImportPreview.Failure($"Can't read this file as {format} {kind}: {ex.Message}");
        }
    }

    private async Task<ImportPreview> PreviewRulesAsync(RulesExportDto dto, CancellationToken ct)
    {
        var existing = await db.Rules.AsNoTracking().ToDictionaryAsync(r => r.Name, ct);
        var changes = dto.Rules.Select(r => existing.TryGetValue(r.Name, out var e)
            ? Compare("Rule", r.Name,
                ("Description", e.Description, r.Description),
                ("Enabled", e.Enabled, r.Enabled),
                ("Priority", e.Priority, r.Priority),
                ("StopOnMatch", e.StopOnMatch, r.StopOnMatch),
                ("DryRun", e.DryRun, r.DryRun),
                ("CronExpression", e.CronExpression, r.CronExpression),
                ("TimeZoneId", e.TimeZoneId, r.TimeZoneId),
                ("ConditionTreeJson", e.ConditionTreeJson, r.ConditionTreeJson),
                ("AdvancedSql", e.AdvancedSql, r.EffectiveAdvancedSql),
                ("UseAdvancedSql", e.UseAdvancedSql, r.UseAdvancedSql),
                ("ActionsJson", e.ActionsJson, r.ActionsJson),
                ("TargetInstanceIdsJson", e.TargetInstanceIdsJson, r.TargetInstanceIdsJson))
            : new ImportChange("Rule", r.Name, ImportChangeKind.Add, [])).ToList();
        return new ImportPreview(changes, null);
    }

    private async Task<ImportPreview> PreviewConfigAsync(ConfigExportDto dto, CancellationToken ct)
    {
        var changes = new List<ImportChange>();

        var instances = await db.Instances.AsNoTracking().ToDictionaryAsync(i => i.Name, ct);
        foreach (var i in dto.Instances)
        {
            changes.Add(instances.TryGetValue(i.Name, out var e)
                ? Compare("Instance", i.Name,
                    ("SourceType", e.SourceType.ToString(), i.SourceType),
                    ("BaseUrl", e.BaseUrl, i.BaseUrl),
                    ("Enabled", e.Enabled, i.Enabled),
                    ("TimeoutSeconds", e.TimeoutSeconds, i.TimeoutSeconds),
                    ("VerifySsl", e.VerifySsl, i.VerifySsl),
                    ("ExtraConfigJson", e.ExtraConfigJson, i.ExtraConfigJson))
                : new ImportChange("Instance", i.Name, ImportChangeKind.Add, []));
        }

        var paths = await db.StoragePaths.AsNoTracking().ToDictionaryAsync(p => p.Name, ct);
        foreach (var s in dto.StoragePaths)
        {
            changes.Add(paths.TryGetValue(s.Name, out var e)
                ? Compare("StoragePath", s.Name,
                    ("Path", e.Path, s.Path),
                    ("Enabled", e.Enabled, s.Enabled),
                    ("FolderSizeScanIntervalMinutes", e.FolderSizeScanIntervalMinutes, s.FolderSizeScanIntervalMinutes))
                : new ImportChange("StoragePath", s.Name, ImportChangeKind.Add, []));
        }

        var settings = await db.AppSettings.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
        var a = dto.AppSettings;
        changes.Add(Compare("Settings", "App settings",
            ("ParallelismLevel", settings.ParallelismLevel.ToString(), a.ParallelismLevel),
            ("GlobalDryRun", settings.GlobalDryRun, a.GlobalDryRun),
            ("GlobalKillSwitch", settings.GlobalKillSwitch, a.GlobalKillSwitch),
            ("Theme", settings.Theme, a.Theme),
            ("LogLevel", settings.LogLevel, a.LogLevel),
            ("TimeZoneId", settings.TimeZoneId, a.TimeZoneId)));

        return new ImportPreview(changes, null);
    }

    private static ImportChange Compare(string section, string name, params (string Field, object? Current, object? Incoming)[] fields)
    {
        var changed = fields.Where(f => !Equals(f.Current, f.Incoming)).Select(f => f.Field).ToList();
        return new ImportChange(section, name, changed.Count == 0 ? ImportChangeKind.Unchanged : ImportChangeKind.Update, changed);
    }
}

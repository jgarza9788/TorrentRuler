namespace TorrentRuler.Infrastructure.Config;

public interface IConfigPortabilityService
{
    Task<string> ExportConfigAsync(ConfigFormat format, CancellationToken ct = default);

    /// <summary>Upserts instances/storage paths/app settings by Name. Never touches credential fields.</summary>
    Task ImportConfigAsync(string content, ConfigFormat format, CancellationToken ct = default);

    Task<string> ExportRulesAsync(ConfigFormat format, CancellationToken ct = default);

    /// <summary>Upserts rules by Name.</summary>
    Task ImportRulesAsync(string content, ConfigFormat format, CancellationToken ct = default);

    /// <summary>What importing <paramref name="content"/> would add / update / leave alone ("config" or "rules"), without writing.</summary>
    Task<ImportPreview> PreviewImportAsync(string content, ConfigFormat format, string kind, CancellationToken ct = default);
}

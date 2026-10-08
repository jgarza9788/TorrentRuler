using TorrentRuler.Core.Domain;
using TorrentRuler.Sources.Http;

namespace TorrentRuler.Sources.Adapters;

/// <summary>Tautulli's /api/v2 (cmd=get_history) is a documented, stable API -- these defaults should work unmodified for most installs.</summary>
public class TautulliAdapter(IInstanceHttpClientFactory httpClientFactory) : RestHistoryAdapterBase(httpClientFactory)
{
    public override SourceType SourceType => SourceType.Tautulli;

    protected override string DefaultHistoryPath => "/api/v2?apikey={apiKey}&cmd=get_history&length=1000";
    protected override string DefaultResultsPath => "response.data.data";

    protected override IReadOnlyDictionary<string, string> DefaultFieldMap => new Dictionary<string, string>
    {
        ["title"] = "full_title",
        ["filePath"] = "file",
        // Plex's ratingKey -- the same id PlexAdapter stores as the library item's external_key.
        ["externalKey"] = "rating_key",
        ["mediaType"] = "media_type",
        ["user"] = "friendly_name",
        ["watchedAt"] = "date",
        ["percent"] = "percent_complete"
    };
}

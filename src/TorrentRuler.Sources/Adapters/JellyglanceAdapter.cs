using TorrentRuler.Core.Domain;
using TorrentRuler.Core.Domain.SourceData;
using TorrentRuler.Sources.Http;

namespace TorrentRuler.Sources.Adapters;

/// <summary>
/// Jellyglance has no single stable public "history" API contract either -- these
/// defaults are a starting point only. Set historyPath/resultsPath/fieldMap in this
/// instance's ExtraConfigJson to match your actual deployment.
/// </summary>
public class JellyglanceAdapter(IInstanceHttpClientFactory httpClientFactory) : RestHistoryAdapterBase(httpClientFactory)
{
    public override SourceType SourceType => SourceType.Jellyglance;

    protected override string DefaultHistoryPath => "/api/history";
    protected override string DefaultResultsPath => "";

    protected override IReadOnlyDictionary<string, string> DefaultFieldMap => new Dictionary<string, string>
    {
        ["title"] = "title",
        ["filePath"] = "path",
        ["user"] = "user",
        ["watchedAt"] = "watchedAt",
        ["percent"] = "percent",
        // Optional: read from the history row when it has them. Point these at your deployment's
        // field names in ExtraConfigJson; a field the row lacks stays NULL.
        ["genres"] = "genres",
        ["officialRating"] = "officialRating",
        ["productionYear"] = "productionYear",
        ["overview"] = "overview"
    };

    protected override void ApplyAuth(HttpRequestMessage request, SourceConnectionInfo connection)
    {
        if (!string.IsNullOrEmpty(connection.ApiKey))
        {
            request.Headers.Add("X-Api-Key", connection.ApiKey);
        }
    }
}

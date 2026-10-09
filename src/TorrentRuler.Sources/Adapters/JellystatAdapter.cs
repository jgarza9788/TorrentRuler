using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using TorrentRuler.Core.Domain;
using TorrentRuler.Core.Domain.SourceData;
using TorrentRuler.Sources.Http;
using TorrentRuler.Sources.Json;

namespace TorrentRuler.Sources.Adapters;

/// <summary>
/// Jellystat does not have one stable, versioned public API contract at the time of
/// writing, so these are best-effort defaults for a typical self-hosted instance, not a
/// guarantee. If they don't match your deployment, override historyPath/resultsPath/
/// fieldMap in this instance's ExtraConfigJson -- no code change needed.
///
/// History is paged (<c>page</c>/<c>size</c> query params, <c>pages</c> in the response), so every
/// page is fetched rather than only the most recent one. History rows carry the Jellyfin item id
/// but no file path, so each distinct id is looked up once via <c>POST /api/getItemDetails</c>
/// and the result is cached for <see cref="ItemDetailsTtl"/>. The same lookup supplies the item's type
/// (<c>media_type</c>), its genres as a comma-separated string, and <c>times_played</c>.
/// </summary>
public class JellystatAdapter(IInstanceHttpClientFactory httpClientFactory) : RestHistoryAdapterBase(httpClientFactory)
{
    private const string ItemDetailsPath = "/api/getItemDetails";
    private const int ItemDetailsParallelism = 4;

    // times_played keeps growing as things are watched, so an hour, not a day.
    private static readonly TimeSpan ItemDetailsTtl = TimeSpan.FromHours(1);

    /// <summary>(instance id, Jellyfin item id) -> what getItemDetails said. Only successful lookups are cached, so a failure is retried next refresh.</summary>
    private readonly ConcurrentDictionary<(int InstanceId, string ItemId), (HistoryItemDetails Details, DateTimeOffset FetchedAt)> _itemDetails = new();

    public override SourceType SourceType => SourceType.Jellystat;

    protected override string DefaultHistoryPath => "/api/getHistory?size=500";
    protected override string DefaultResultsPath => "results";

    protected override IReadOnlyDictionary<string, string> DefaultFieldMap => new Dictionary<string, string>
    {
        ["title"] = "NowPlayingItemName",
        ["filePath"] = "FullPath",
        // For an episode NowPlayingItemId is the series; EpisodeId is the episode itself, and
        // the episode is what has a file. Movies have no EpisodeId, so they fall through.
        ["externalKey"] = "EpisodeId|NowPlayingItemId",
        ["user"] = "UserName",
        ["watchedAt"] = "ActivityDateInserted",
        ["percent"] = "PercentComplete",
        ["seriesName"] = "SeriesName",
        // Properties of the getItemDetails response.
        ["itemPath"] = "Path",
        ["itemType"] = "Type",
        ["itemGenres"] = "Genres",
        ["itemTimesPlayed"] = "times_played",
        ["itemCommunityRating"] = "CommunityRating",
        ["itemCriticRating"] = "CriticRating",
        ["itemOfficialRating"] = "OfficialRating",
        ["itemProductionYear"] = "ProductionYear",
        ["itemOverview"] = "Overview",
        ["itemSortName"] = "SortName",
        ["itemOriginalTitle"] = "OriginalTitle",
        ["itemSeriesName"] = "SeriesName",
        ["itemSeasonNumber"] = "ParentIndexNumber",
        ["itemEpisodeNumber"] = "IndexNumber",
        ["itemRunTimeTicks"] = "RunTimeTicks",
        ["itemPremiereDate"] = "PremiereDate",
        ["itemStudios"] = "Studios"
    };

    protected override void ApplyAuth(HttpRequestMessage request, SourceConnectionInfo connection)
    {
        if (!string.IsNullOrEmpty(connection.ApiKey))
        {
            request.Headers.Add("x-api-token", connection.ApiKey);
        }
    }

    protected override string PageUrl(string historyUrl, int page) =>
        $"{historyUrl}{(historyUrl.Contains('?') ? '&' : '?')}page={page}";

    protected override int? PageCount(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("pages", out var pages)
        && pages.ValueKind == JsonValueKind.Number
        && pages.TryGetInt32(out var count)
            ? count
            : null;

    protected override async Task<IReadOnlyDictionary<string, HistoryItemDetails>> ResolveItemDetailsAsync(
        HttpClient client, SourceConnectionInfo connection, RestHistoryConfig config,
        IReadOnlyCollection<string> itemIds, CancellationToken ct)
    {
        string Field(string key, string fallback) => config.FieldMap.GetValueOrDefault(key) is { Length: > 0 } f ? f : fallback;
        var pathField = Field("itemPath", "Path");
        var typeField = Field("itemType", "Type");
        var genresField = Field("itemGenres", "Genres");
        var timesPlayedField = Field("itemTimesPlayed", "times_played");
        string? Text(JsonElement root, string key, string fallback) => JsonPathResolver.FindString(root, Field(key, fallback));
        double? Number(JsonElement root, string key, string fallback) => JsonPathResolver.ToDouble(JsonPathResolver.FindElement(root, Field(key, fallback)));
        int? Whole(JsonElement root, string key, string fallback) => Number(root, key, fallback) is { } n ? (int)Math.Round(n) : null;
        var resolved = new ConcurrentDictionary<string, HistoryItemDetails>(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;

        var toFetch = new List<string>();
        foreach (var id in itemIds)
        {
            if (_itemDetails.TryGetValue((connection.InstanceId, id), out var cached) && now - cached.FetchedAt < ItemDetailsTtl)
            {
                resolved[id] = cached.Details;
            }
            else
            {
                toFetch.Add(id);
            }
        }

        var url = $"{connection.BaseUrl.TrimEnd('/')}{ItemDetailsPath}";
        await Parallel.ForEachAsync(toFetch, new ParallelOptions { MaxDegreeOfParallelism = ItemDetailsParallelism, CancellationToken = ct },
            async (id, token) =>
            {
                try
                {
                    using var cts = HttpTimeouts.Create(token, connection.TimeoutSeconds);
                    // A dictionary, not an anonymous object: JsonContent camel-cases property names,
                    // and Jellystat reads req.body.Id (capital I) -- "id" is ignored.
                    using var request = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = JsonContent.Create(new Dictionary<string, string> { ["Id"] = id })
                    };
                    ApplyAuth(request, connection);

                    using var response = await client.SendAsync(request, cts.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        return;
                    }

                    using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
                    var root = doc.RootElement;
                    var details = new HistoryItemDetails
                    {
                        Path = JsonPathResolver.FindString(root, pathField),
                        MediaType = JsonPathResolver.FindString(root, typeField),
                        Genres = JsonPathResolver.ToCommaList(JsonPathResolver.FindElement(root, genresField)),
                        TimesPlayed = JsonPathResolver.ToDouble(JsonPathResolver.FindElement(root, timesPlayedField)) is { } n ? (long)Math.Round(n) : null,
                        CommunityRating = Number(root, "itemCommunityRating", "CommunityRating"),
                        CriticRating = Number(root, "itemCriticRating", "CriticRating"),
                        OfficialRating = Text(root, "itemOfficialRating", "OfficialRating"),
                        ProductionYear = Whole(root, "itemProductionYear", "ProductionYear"),
                        Overview = Text(root, "itemOverview", "Overview"),
                        SortName = Text(root, "itemSortName", "SortName"),
                        OriginalTitle = Text(root, "itemOriginalTitle", "OriginalTitle"),
                        SeriesName = Text(root, "itemSeriesName", "SeriesName"),
                        SeasonNumber = Whole(root, "itemSeasonNumber", "ParentIndexNumber"),
                        EpisodeNumber = Whole(root, "itemEpisodeNumber", "IndexNumber"),
                        RuntimeMinutes = Number(root, "itemRunTimeTicks", "RunTimeTicks") / 600_000_000.0,
                        PremiereDate = ParseDate(Text(root, "itemPremiereDate", "PremiereDate")),
                        Studios = JsonPathResolver.ToCommaList(JsonPathResolver.FindElement(root, Field("itemStudios", "Studios")))
                    };

                    // A response that told us nothing isn't worth caching (and isn't worth a row of NULLs).
                    if (details != new HistoryItemDetails())
                    {
                        resolved[id] = details;
                        _itemDetails[(connection.InstanceId, id)] = (details, DateTimeOffset.UtcNow);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException
                                               || (ex is OperationCanceledException && !token.IsCancellationRequested))
                {
                    // One item that Jellystat can't describe (deleted, not yet synced, timed out)
                    // only leaves that item's rows without a path; the snapshot can still link
                    // them to a library file through external_key.
                }
            });

        return resolved;
    }
}

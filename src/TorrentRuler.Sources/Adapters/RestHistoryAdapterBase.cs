using System.Diagnostics;
using System.Text.Json;
using TorrentRuler.Core.Domain;
using TorrentRuler.Core.Domain.SourceData;
using TorrentRuler.Core.Interfaces;
using TorrentRuler.Sources.Http;
using TorrentRuler.Sources.Json;

namespace TorrentRuler.Sources.Adapters;

/// <summary>
/// Shared "fetch a JSON array of watch/play events from a REST endpoint" logic for
/// Tautulli, Jellystat, and Jellyglance -- they differ only in URL shape, auth
/// placement, and JSON field names, all of which are supplied by the subclass (and
/// further overridable per-instance via ExtraConfigJson, see RestHistoryConfig).
/// </summary>
public abstract class RestHistoryAdapterBase(IInstanceHttpClientFactory httpClientFactory) : ISourceAdapter
{
    public abstract SourceType SourceType { get; }
    protected abstract string DefaultHistoryPath { get; }
    protected abstract string DefaultResultsPath { get; }
    protected abstract IReadOnlyDictionary<string, string> DefaultFieldMap { get; }

    /// <summary>Substitutes {apiKey} into the configured history path for sources that take the key as a query parameter.</summary>
    protected virtual string BuildUrl(SourceConnectionInfo connection, string historyPath) =>
        $"{connection.BaseUrl.TrimEnd('/')}{historyPath.Replace("{apiKey}", Uri.EscapeDataString(connection.ApiKey ?? string.Empty))}";

    /// <summary>Override to attach a header-based API key instead of (or in addition to) a query parameter.</summary>
    protected virtual void ApplyAuth(HttpRequestMessage request, SourceConnectionInfo connection)
    {
    }

    public async Task<SourceFetchResult> FetchAsync(SourceConnectionInfo connection, CancellationToken ct = default)
    {
        var config = RestHistoryConfig.Parse(connection.ExtraConfigJson, DefaultHistoryPath, DefaultResultsPath, DefaultFieldMap);
        var records = await FetchHistoryAsync(connection, config, ct);
        return new SourceFetchResult { WatchHistory = records };
    }

    public virtual async Task<ConnectionTestResult> TestConnectionAsync(SourceConnectionInfo connection, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var config = RestHistoryConfig.Parse(connection.ExtraConfigJson, DefaultHistoryPath, DefaultResultsPath, DefaultFieldMap);
            var records = await FetchHistoryAsync(connection, config, ct);
            return new ConnectionTestResult { Success = true, Message = $"Connected ({records.Count} history record(s) found).", Duration = sw.Elapsed };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult { Success = false, Message = ex.Message, Duration = sw.Elapsed };
        }
    }

    /// <summary>Hard stop for paged sources, so a server that misreports its page count can't loop forever.</summary>
    protected const int MaxPages = 1000;

    /// <summary>
    /// The URL of one page of history. The default ignores <paramref name="page"/> because the
    /// default source is not paged (see <see cref="PageCount"/>).
    /// </summary>
    protected virtual string PageUrl(string historyUrl, int page) => historyUrl;

    /// <summary>
    /// Total number of pages the response says exist, or null for a source that returns all of its
    /// history in one response. Paging stops at this count, or at the first empty page.
    /// </summary>
    protected virtual int? PageCount(JsonElement root) => null;

    /// <summary>
    /// Looks up a file path for each item id whose history rows came back without one. The default
    /// can't; a source with a per-item details endpoint overrides it. A missing entry in the result
    /// just leaves that row's path NULL, so one failed lookup never fails the fetch.
    /// </summary>
    protected virtual Task<IReadOnlyDictionary<string, string>> ResolveItemPathsAsync(
        HttpClient client, SourceConnectionInfo connection, RestHistoryConfig config,
        IReadOnlyCollection<string> itemIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

    private async Task<List<WatchHistoryRecord>> FetchHistoryAsync(SourceConnectionInfo connection, RestHistoryConfig config, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient(connection);
        var historyUrl = BuildUrl(connection, config.HistoryPath);

        // Cloned so they outlive each page's JsonDocument; read into records once the paths are known.
        var items = new List<JsonElement>();
        for (var page = 1; page <= MaxPages; page++)
        {
            // Per request, not per fetch: a paged history is many requests, and TimeoutSeconds is
            // meant to bound one slow server response, not the whole history download.
            using var cts = HttpTimeouts.Create(ct, connection.TimeoutSeconds);
            using var request = new HttpRequestMessage(HttpMethod.Get, PageUrl(historyUrl, page));
            ApplyAuth(request, connection);

            using var response = await client.SendAsync(request, cts.Token);
            await AdapterHttp.EnsureSuccessAsync(response, SourceType.ToString(), cts.Token);

            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);

            var pageItems = 0;
            if (JsonPathResolver.Resolve(doc.RootElement, config.ResultsPath) is { ValueKind: JsonValueKind.Array } arr)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    items.Add(item.Clone());
                    pageItems++;
                }
            }

            if (pageItems == 0 || PageCount(doc.RootElement) is not { } pages || page >= pages)
            {
                break;
            }
        }

        var keyField = config.FieldMap.GetValueOrDefault("externalKey");
        var pathField = config.FieldMap.GetValueOrDefault("filePath");
        var idsWithoutPath = items
            .Where(i => string.IsNullOrEmpty(JsonPathResolver.GetString(i, pathField)))
            .Select(i => JsonPathResolver.GetString(i, keyField))
            .OfType<string>()
            .Where(id => id.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var resolvedPaths = idsWithoutPath.Count > 0
            ? await ResolveItemPathsAsync(client, connection, config, idsWithoutPath, ct)
            : new Dictionary<string, string>();

        return items.Select(item =>
        {
            var externalKey = JsonPathResolver.GetString(item, keyField);
            var filePath = JsonPathResolver.GetString(item, pathField);
            if (string.IsNullOrEmpty(filePath) && externalKey is not null)
            {
                filePath = resolvedPaths.GetValueOrDefault(externalKey);
            }

            return new WatchHistoryRecord
            {
                InstanceId = connection.InstanceId,
                InstanceName = connection.InstanceName,
                SourceType = SourceType,
                ExternalKey = externalKey,
                MediaTitle = JsonPathResolver.GetString(item, config.FieldMap.GetValueOrDefault("title")),
                FilePath = filePath,
                UserName = JsonPathResolver.GetString(item, config.FieldMap.GetValueOrDefault("user")),
                WatchedAt = JsonPathResolver.GetUnixSeconds(item, config.FieldMap.GetValueOrDefault("watchedAt")),
                PercentComplete = JsonPathResolver.GetDouble(item, config.FieldMap.GetValueOrDefault("percent"))
            };
        }).ToList();
    }
}

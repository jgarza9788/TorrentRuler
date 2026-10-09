using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TorrentRuler.Core.Domain;
using TorrentRuler.Core.Domain.SourceData;
using TorrentRuler.Core.Interfaces;
using TorrentRuler.Sources.Http;

namespace TorrentRuler.Sources.Adapters;

public class JellyfinAdapter(IInstanceHttpClientFactory httpClientFactory) : ISourceAdapter
{
    public SourceType SourceType => SourceType.Jellyfin;

    public async Task<ConnectionTestResult> TestConnectionAsync(SourceConnectionInfo connection, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = httpClientFactory.CreateClient(connection);
            using var cts = HttpTimeouts.Create(ct, connection.TimeoutSeconds);
            using var request = BuildRequest(connection, "/System/Info");
            using var response = await client.SendAsync(request, cts.Token);
            await AdapterHttp.EnsureSuccessAsync(response, "Jellyfin", cts.Token);

            return new ConnectionTestResult { Success = true, Message = "Connected to Jellyfin.", Duration = sw.Elapsed };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult { Success = false, Message = ex.Message, Duration = sw.Elapsed };
        }
    }

    public async Task<SourceFetchResult> FetchAsync(SourceConnectionInfo connection, CancellationToken ct = default)
    {
        using var client = httpClientFactory.CreateClient(connection);
        using var cts = HttpTimeouts.Create(ct, connection.TimeoutSeconds);

        using var request = BuildRequest(connection, $"/Items?Recursive=true&IncludeItemTypes=Movie,Episode&Fields={ItemFields},DateCreated");
        using var response = await client.SendAsync(request, cts.Token);
        await AdapterHttp.EnsureSuccessAsync(response, "Jellyfin", cts.Token);
        var payload = await response.Content.ReadFromJsonAsync<JellyfinItemsResponse>(cancellationToken: cts.Token);

        var items = (payload?.Items ?? []).Select(i => new MediaItemRecord
        {
            InstanceId = connection.InstanceId,
            InstanceName = connection.InstanceName,
            SourceType = SourceType.Jellyfin,
            ExternalKey = i.Id,
            Title = i.Name,
            MediaType = i.Type,
            Genres = JoinGenres(i.Genres),
            CommunityRating = i.CommunityRating,
            CriticRating = i.CriticRating,
            OfficialRating = i.OfficialRating,
            ProductionYear = i.ProductionYear,
            Overview = i.Overview,
            SortName = i.SortName,
            OriginalTitle = i.OriginalTitle,
            SeriesName = i.SeriesName,
            SeasonNumber = i.ParentIndexNumber,
            EpisodeNumber = i.IndexNumber,
            RuntimeMinutes = i.RunTimeTicks / TicksPerMinute,
            PremiereDate = i.PremiereDate,
            Studios = JoinNames(i.Studios?.Select(s => s.Name)),
            FilePaths = string.IsNullOrEmpty(i.Path) ? [] : [i.Path],
            AddedAt = i.DateCreated
        }).ToList();

        var history = await FetchWatchHistoryAsync(client, connection, ct);
        return new SourceFetchResult { MediaItems = items, WatchHistory = history };
    }

    /// <summary>The item fields both the library fetch and the per-user fetches ask for.</summary>
    private const string ItemFields = "Path,Genres,CommunityRating,CriticRating,Overview,OriginalTitle,SortName,Studios";

    private const double TicksPerMinute = 600_000_000.0;

    private static string? JoinGenres(List<string>? genres) => JoinNames(genres);

    private static string? JoinNames(IEnumerable<string>? names) =>
        names?.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList() is { Count: > 0 } list
            ? string.Join(",", list)
            : null;

    /// <summary>
    /// Watch history from each user's own play data (<c>UserData</c>): one row per user per item they
    /// played or started. Listing users needs an admin key; without one this returns nothing rather than
    /// failing the whole fetch, since the library itself is the adapter's main job. A user whose items
    /// can't be read is skipped the same way.
    /// </summary>
    private async Task<List<WatchHistoryRecord>> FetchWatchHistoryAsync(HttpClient client, SourceConnectionInfo connection, CancellationToken ct)
    {
        var history = new List<WatchHistoryRecord>();

        List<JellyfinUser> users;
        try
        {
            using var cts = HttpTimeouts.Create(ct, connection.TimeoutSeconds);
            using var request = BuildRequest(connection, "/Users");
            using var response = await client.SendAsync(request, cts.Token);
            await AdapterHttp.EnsureSuccessAsync(response, "Jellyfin", cts.Token);
            users = await response.Content.ReadFromJsonAsync<List<JellyfinUser>>(cancellationToken: cts.Token) ?? [];
        }
        catch (Exception ex) when (IsRecoverable(ex, ct))
        {
            return history;
        }

        foreach (var user in users.Where(u => !string.IsNullOrEmpty(u.Id)))
        {
            try
            {
                // Per request, like the library fetch: TimeoutSeconds bounds one response, not the whole refresh.
                using var cts = HttpTimeouts.Create(ct, connection.TimeoutSeconds);
                using var request = BuildRequest(connection,
                    $"/Users/{Uri.EscapeDataString(user.Id)}/Items?Recursive=true&IncludeItemTypes=Movie,Episode&Fields={ItemFields}&EnableImages=false");
                using var response = await client.SendAsync(request, cts.Token);
                await AdapterHttp.EnsureSuccessAsync(response, "Jellyfin", cts.Token);
                var payload = await response.Content.ReadFromJsonAsync<JellyfinItemsResponse>(cancellationToken: cts.Token);

                foreach (var i in payload?.Items ?? [])
                {
                    if (i.UserData is not { } data || !(data.Played || data.PlayCount > 0 || data.PlaybackPositionTicks > 0))
                    {
                        continue; // never started by this user
                    }

                    history.Add(new WatchHistoryRecord
                    {
                        InstanceId = connection.InstanceId,
                        InstanceName = connection.InstanceName,
                        SourceType = SourceType.Jellyfin,
                        ExternalKey = i.Id,
                        MediaTitle = i.Name,
                        MediaType = i.Type,
                        Genres = JoinGenres(i.Genres),
                        CommunityRating = i.CommunityRating,
                        CriticRating = i.CriticRating,
                        OfficialRating = i.OfficialRating,
                        ProductionYear = i.ProductionYear,
                        Overview = i.Overview,
                        SortName = i.SortName,
                        OriginalTitle = i.OriginalTitle,
                        SeriesName = i.SeriesName,
                        SeasonNumber = i.ParentIndexNumber,
                        EpisodeNumber = i.IndexNumber,
                        RuntimeMinutes = i.RunTimeTicks / TicksPerMinute,
                        PremiereDate = i.PremiereDate,
                        Studios = JoinNames(i.Studios?.Select(s => s.Name)),
                        FilePath = i.Path,
                        UserName = user.Name,
                        WatchedAt = data.LastPlayedDate,
                        // Jellyfin leaves PlayedPercentage out for a finished item; finished means all of it.
                        PercentComplete = data.PlayedPercentage ?? (data.Played ? 100.0 : null),
                        TimesPlayed = data.PlayCount
                    });
                }
            }
            catch (Exception ex) when (IsRecoverable(ex, ct))
            {
                // This user's data is unavailable; the other users' history is still worth having.
            }
        }

        return history;
    }

    /// <summary>A failed request or unreadable response is recoverable; the caller cancelling the whole fetch is not.</summary>
    private static bool IsRecoverable(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);

    /// <summary>
    /// Auth via the <c>Authorization: MediaBrowser Token="&lt;key&gt;"</c> scheme -- the original
    /// Emby/Jellyfin mechanism, accepted by every version from 10.x through 12.x. The legacy
    /// <c>?api_key=</c> query param and the <c>X-Emby-Token</c> header are both rejected
    /// (HTTP 401) by Jellyfin 12, so they are not used.
    /// </summary>
    private static HttpRequestMessage BuildRequest(SourceConnectionInfo connection, string pathAndQuery)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{connection.BaseUrl.TrimEnd('/')}{pathAndQuery}");
        request.Headers.Add("Accept", "application/json");
        if (!string.IsNullOrEmpty(connection.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"MediaBrowser Token=\"{connection.ApiKey}\"");
        }
        return request;
    }

    private sealed class JellyfinItemsResponse
    {
        [JsonPropertyName("Items")] public List<JellyfinItem> Items { get; set; } = [];
    }

    private sealed class JellyfinItem
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = "";
        [JsonPropertyName("Name")] public string Name { get; set; } = "";
        [JsonPropertyName("Type")] public string Type { get; set; } = "";
        [JsonPropertyName("Path")] public string? Path { get; set; }
        [JsonPropertyName("Genres")] public List<string>? Genres { get; set; }
        [JsonPropertyName("CommunityRating")] public double? CommunityRating { get; set; }
        [JsonPropertyName("CriticRating")] public double? CriticRating { get; set; }
        [JsonPropertyName("OfficialRating")] public string? OfficialRating { get; set; }
        [JsonPropertyName("ProductionYear")] public int? ProductionYear { get; set; }
        [JsonPropertyName("Overview")] public string? Overview { get; set; }
        [JsonPropertyName("SortName")] public string? SortName { get; set; }
        [JsonPropertyName("OriginalTitle")] public string? OriginalTitle { get; set; }
        [JsonPropertyName("SeriesName")] public string? SeriesName { get; set; }
        [JsonPropertyName("ParentIndexNumber")] public int? ParentIndexNumber { get; set; }
        [JsonPropertyName("IndexNumber")] public int? IndexNumber { get; set; }
        [JsonPropertyName("RunTimeTicks")] public long? RunTimeTicks { get; set; }
        [JsonPropertyName("PremiereDate")] public DateTimeOffset? PremiereDate { get; set; }
        [JsonPropertyName("Studios")] public List<JellyfinNamed>? Studios { get; set; }
        [JsonPropertyName("DateCreated")] public DateTimeOffset? DateCreated { get; set; }
        [JsonPropertyName("UserData")] public JellyfinUserData? UserData { get; set; }
    }

    private sealed class JellyfinNamed
    {
        [JsonPropertyName("Name")] public string Name { get; set; } = "";
    }

    private sealed class JellyfinUser
    {
        [JsonPropertyName("Id")] public string Id { get; set; } = "";
        [JsonPropertyName("Name")] public string Name { get; set; } = "";
    }

    private sealed class JellyfinUserData
    {
        [JsonPropertyName("PlayedPercentage")] public double? PlayedPercentage { get; set; }
        [JsonPropertyName("PlaybackPositionTicks")] public long PlaybackPositionTicks { get; set; }
        [JsonPropertyName("PlayCount")] public long PlayCount { get; set; }
        [JsonPropertyName("Played")] public bool Played { get; set; }
        [JsonPropertyName("LastPlayedDate")] public DateTimeOffset? LastPlayedDate { get; set; }
    }
}

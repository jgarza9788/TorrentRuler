using System.Net;
using System.Text;
using System.Text.Json;
using TorrentRuler.Core.Domain;
using TorrentRuler.Core.Domain.SourceData;
using TorrentRuler.Sources.Adapters;
using TorrentRuler.Tests.TestHelpers;
using Xunit;

namespace TorrentRuler.Tests.Sources;

public class JellystatAdapterTests
{
    private static readonly SourceConnectionInfo Connection = new()
    {
        InstanceId = 7,
        InstanceName = "js1",
        SourceType = SourceType.Jellystat,
        BaseUrl = "http://jellystat:3000",
        ApiKey = "key",
        TimeoutSeconds = 5,
        VerifySsl = true
    };

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task FetchAsync_ReadsEveryPage_AndResolvesPathsThroughItemDetails()
    {
        var detailRequests = new List<string>();
        var historyPages = new List<string>();
        var handler = new FakeHttpMessageHandler(req =>
        {
            Assert.Equal("key", req.Headers.GetValues("x-api-token").Single());

            if (req.RequestUri!.AbsolutePath == "/api/getHistory")
            {
                historyPages.Add(req.RequestUri.Query);
                return req.RequestUri.Query.Contains("page=1")
                    ? Json("""
                        {"current_page":1,"pages":2,"results":[
                          {"NowPlayingItemId":"movie1","NowPlayingItemName":"Foo","UserName":"alice","ActivityDateInserted":"2026-01-01T00:00:00Z","PercentComplete":90},
                          {"NowPlayingItemId":"series1","EpisodeId":"ep1","NowPlayingItemName":"Bar S01E01","UserName":"bob","ActivityDateInserted":"2026-01-02T00:00:00Z"}
                        ]}
                        """)
                    : Json("""
                        {"current_page":2,"pages":2,"results":[
                          {"NowPlayingItemId":"movie1","NowPlayingItemName":"Foo","UserName":"bob","ActivityDateInserted":"2026-01-03T00:00:00Z"}
                        ]}
                        """);
            }

            Assert.Equal("/api/getItemDetails", req.RequestUri.AbsolutePath);
            Assert.Equal(HttpMethod.Post, req.Method);
            var id = JsonDocument.Parse(req.Content!.ReadAsStringAsync().Result).RootElement.GetProperty("Id").GetString()!;
            lock (detailRequests)
            {
                detailRequests.Add(id);
            }
            return id switch
            {
                "movie1" => Json("""[{"Id":"movie1","Name":"Foo","Type":"Movie","Genres":["Drama","Sci-Fi"],"times_played":12,"Path":"/media/movies/Foo/Foo.mkv","Size":123}]"""),
                "ep1" => Json("""[{"EpisodeId":"ep1","FileName":"Bar","Type":"Episode","Genres":[],"Path":"/media/tv/Bar/S01E01.mkv"}]"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });

        var adapter = new JellystatAdapter(new StubInstanceHttpClientFactory(handler));
        var result = await adapter.FetchAsync(Connection);

        Assert.Equal(2, historyPages.Count);
        Assert.Equal(3, result.WatchHistory.Count);

        // Each distinct item is looked up once, however many plays it has.
        Assert.Equal(["ep1", "movie1"], detailRequests.Order());

        var episode = result.WatchHistory.Single(w => w.UserName == "bob" && w.MediaTitle == "Bar S01E01");
        Assert.Equal("ep1", episode.ExternalKey);
        Assert.Equal("/media/tv/Bar/S01E01.mkv", episode.FilePath);

        Assert.All(result.WatchHistory.Where(w => w.ExternalKey == "movie1"),
            w => Assert.Equal("/media/movies/Foo/Foo.mkv", w.FilePath));
    }

    [Fact]
    public async Task FetchAsync_TakesTypeGenresAndTimesPlayedFromItemDetails()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/getHistory")
            {
                return Json("""{"pages":1,"results":[{"NowPlayingItemId":"m1","UserName":"a"},{"NowPlayingItemId":"e1","EpisodeId":"e1","UserName":"a"},{"NowPlayingItemId":"x1","UserName":"a"}]}""");
            }

            var id = JsonDocument.Parse(req.Content!.ReadAsStringAsync().Result).RootElement.GetProperty("Id").GetString();
            return id switch
            {
                "m1" => Json("""[{"Type":"Movie","Genres":["Drama","Sci-Fi"],"times_played":"12","Path":"/m.mkv"}]"""),
                // Genres as objects, and a nested "Type" that must not win over the item's own.
                "e1" => Json("""[{"Type":"Episode","Genres":[{"Name":"Comedy"}],"Path":"/e.mkv","MediaStreams":[{"Type":"Video"}]}]"""),
                // No genres, no time played, no type: all three stay null rather than empty.
                _ => Json("""[{"Path":"/x.mkv"}]""")
            };
        });

        var result = await new JellystatAdapter(new StubInstanceHttpClientFactory(handler)).FetchAsync(Connection);

        var movie = result.WatchHistory.Single(w => w.ExternalKey == "m1");
        Assert.Equal(("Movie", "Drama,Sci-Fi", 12L), (movie.MediaType, movie.Genres, movie.TimesPlayed));
        var episode = result.WatchHistory.Single(w => w.ExternalKey == "e1");
        Assert.Equal(("Episode", "Comedy", (long?)null), (episode.MediaType, episode.Genres, episode.TimesPlayed));
        var bare = result.WatchHistory.Single(w => w.ExternalKey == "x1");
        Assert.Equal(((string?)null, (string?)null, (long?)null), (bare.MediaType, bare.Genres, bare.TimesPlayed));
    }

    [Fact]
    public async Task FetchAsync_LooksUpDetailsEvenWhenHistoryAlreadyHasAPath()
    {
        var lookups = 0;
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/getHistory")
            {
                return Json("""{"pages":1,"results":[{"NowPlayingItemId":"m1","FullPath":"/from/history.mkv","UserName":"a"}]}""");
            }
            Interlocked.Increment(ref lookups);
            return Json("""[{"Type":"Movie","Path":"/from/details.mkv"}]""");
        });

        var result = await new JellystatAdapter(new StubInstanceHttpClientFactory(handler)).FetchAsync(Connection);

        var row = Assert.Single(result.WatchHistory);
        Assert.Equal(1, lookups);
        Assert.Equal("Movie", row.MediaType);
        Assert.Equal("/from/history.mkv", row.FilePath); // a path the history already carried wins
    }

    [Fact]
    public async Task FetchAsync_CachesItemPaths_AndKeepsRowsWhoseLookupFails()
    {
        var detailCalls = 0;
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/getHistory")
            {
                return Json("""
                    {"pages":1,"results":[
                      {"NowPlayingItemId":"movie1","UserName":"alice"},
                      {"NowPlayingItemId":"gone","UserName":"alice"}
                    ]}
                    """);
            }

            Interlocked.Increment(ref detailCalls);
            var id = JsonDocument.Parse(req.Content!.ReadAsStringAsync().Result).RootElement.GetProperty("Id").GetString();
            return id == "movie1"
                ? Json("""[{"Path":"/media/movies/Foo.mkv"}]""")
                : new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var adapter = new JellystatAdapter(new StubInstanceHttpClientFactory(handler));

        var first = await adapter.FetchAsync(Connection);
        Assert.Equal(2, first.WatchHistory.Count);
        Assert.Null(first.WatchHistory.Single(w => w.ExternalKey == "gone").FilePath);
        Assert.Equal(2, detailCalls);

        // movie1 is cached; only the failed lookup is retried.
        await adapter.FetchAsync(Connection);
        Assert.Equal(3, detailCalls);
    }
}

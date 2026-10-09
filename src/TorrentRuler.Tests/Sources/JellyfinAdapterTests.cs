using System.Net;
using System.Text;
using TorrentRuler.Core.Domain;
using TorrentRuler.Core.Domain.SourceData;
using TorrentRuler.Sources.Adapters;
using TorrentRuler.Tests.TestHelpers;
using Xunit;

namespace TorrentRuler.Tests.Sources;

public class JellyfinAdapterTests
{
    private static readonly SourceConnectionInfo Connection = new()
    {
        InstanceId = 3,
        InstanceName = "jf1",
        SourceType = SourceType.Jellyfin,
        BaseUrl = "http://jellyfin:8096",
        ApiKey = "key",
        TimeoutSeconds = 5,
        VerifySsl = true
    };

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string LibraryJson = """
        {"Items":[
          {"Id":"m1","Name":"Foo","Type":"Movie","Path":"/media/Foo.mkv","Genres":["Drama","Sci-Fi"],"CommunityRating":7.4,"CriticRating":85},
          {"Id":"e1","Name":"Bar S01E01","Type":"Episode","Path":"/media/Bar.mkv","Genres":[]}
        ]}
        """;

    private static FakeHttpMessageHandler Server(Func<string, HttpResponseMessage?> perUser, bool usersForbidden = false) => new(req =>
    {
        var path = req.RequestUri!.AbsolutePath;
        if (path == "/Items")
        {
            return Json(LibraryJson);
        }
        if (path == "/Users")
        {
            return usersForbidden
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : Json("""[{"Id":"u-alice","Name":"alice"},{"Id":"u-bob","Name":"bob"}]""");
        }
        if (path.StartsWith("/Users/") && path.EndsWith("/Items"))
        {
            return perUser(path.Split('/')[2]) ?? Json("""{"Items":[]}""");
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    });

    [Fact]
    public async Task FetchAsync_TurnsEachUsersPlayedItemsIntoHistoryRows()
    {
        var handler = Server(user => user == "u-alice"
            ? Json("""
                {"Items":[
                  {"Id":"m1","Name":"Foo","Type":"Movie","Path":"/media/Foo.mkv","Genres":["Drama","Sci-Fi"],"CommunityRating":7.4,"CriticRating":85,
                   "UserData":{"PlayedPercentage":87.5,"PlaybackPositionTicks":63576250000,"PlayCount":2,"Played":true,"LastPlayedDate":"2026-10-09T00:00:21.2336812Z"}},
                  {"Id":"e1","Name":"Bar S01E01","Type":"Episode","Path":"/media/Bar.mkv",
                   "UserData":{"PlayCount":1,"Played":true,"LastPlayedDate":"2026-10-01T12:00:00Z"}}
                ]}
                """)
            : null);

        var result = await new JellyfinAdapter(new StubInstanceHttpClientFactory(handler)).FetchAsync(Connection);

        Assert.Equal(2, result.MediaItems.Count);
        Assert.Equal(2, result.WatchHistory.Count);

        // Ratings and genres are the item's, so they appear on the library item and on every watch row for it.
        var library = result.MediaItems.Single(m => m.ExternalKey == "m1");
        Assert.Equal((7.4, 85.0, "Drama,Sci-Fi"), (library.CommunityRating, library.CriticRating, library.Genres));
        Assert.Equal((null, null, null), (result.MediaItems.Single(m => m.ExternalKey == "e1").CommunityRating, result.MediaItems.Single(m => m.ExternalKey == "e1").CriticRating, result.MediaItems.Single(m => m.ExternalKey == "e1").Genres));

        var movie = result.WatchHistory.Single(w => w.ExternalKey == "m1");
        Assert.Equal(("alice", "Foo", "Movie", "Drama,Sci-Fi", 87.5, 2L), (movie.UserName, movie.MediaTitle, movie.MediaType, movie.Genres, movie.PercentComplete, movie.TimesPlayed));
        Assert.Equal((7.4, 85.0), (movie.CommunityRating, movie.CriticRating));
        Assert.Equal(DateTimeOffset.Parse("2026-10-09T00:00:21.2336812Z"), movie.WatchedAt);
        Assert.Equal((3, "jf1", SourceType.Jellyfin, "/media/Foo.mkv"), (movie.InstanceId, movie.InstanceName, movie.SourceType, movie.FilePath));

        // A fully played item with no recorded percentage counts as 100% watched.
        Assert.Equal(100.0, result.WatchHistory.Single(w => w.ExternalKey == "e1").PercentComplete);
    }

    [Fact]
    public async Task FetchAsync_CarriesDescriptiveMetadataOntoLibraryAndHistoryRows()
    {
        const string item = """
            {"Id":"e9","Name":"Pilot","Type":"Episode","Path":"/media/Pilot.mkv","OfficialRating":"TV-MA","ProductionYear":2008,
             "Overview":"A teacher turns to crime.","SortName":"pilot","OriginalTitle":"Pilot (orig)","SeriesName":"Breaking Bad",
             "ParentIndexNumber":1,"IndexNumber":2,"RunTimeTicks":36000000000,"PremiereDate":"2008-01-20T00:00:00.0000000Z",
             "Studios":[{"Name":"Sony"},{"Name":"AMC"}],
             "UserData":{"PlayCount":1,"Played":true}}
            """;
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/Items" => Json($$"""{"Items":[{{item}}]}"""),
            "/Users" => Json("""[{"Id":"u1","Name":"alice"}]"""),
            _ => Json($$"""{"Items":[{{item}}]}""")
        });

        var result = await new JellyfinAdapter(new StubInstanceHttpClientFactory(handler)).FetchAsync(Connection);

        var library = result.MediaItems.Single();
        Assert.Equal(("TV-MA", 2008, "A teacher turns to crime.", "pilot", "Pilot (orig)"),
            (library.OfficialRating, library.ProductionYear, library.Overview, library.SortName, library.OriginalTitle));
        Assert.Equal(("Breaking Bad", 1, 2, 60.0, "Sony,AMC"),
            (library.SeriesName, library.SeasonNumber, library.EpisodeNumber, library.RuntimeMinutes, library.Studios));
        Assert.Equal(DateTimeOffset.Parse("2008-01-20T00:00:00Z"), library.PremiereDate);

        var watched = result.WatchHistory.Single();
        Assert.Equal(("TV-MA", 2008, "Breaking Bad"), (watched.OfficialRating, watched.ProductionYear, watched.SeriesName));
    }

    [Fact]
    public async Task FetchAsync_SkipsItemsAUserNeverStarted_AndKeepsPartiallyWatchedOnes()
    {
        var handler = Server(user => user == "u-bob"
            ? Json("""
                {"Items":[
                  {"Id":"m1","Name":"Foo","Type":"Movie","UserData":{"PlayCount":0,"Played":false,"PlaybackPositionTicks":0}},
                  {"Id":"e1","Name":"Bar","Type":"Episode","UserData":{"PlayCount":0,"Played":false,"PlaybackPositionTicks":9000000000,"PlayedPercentage":12.5}},
                  {"Id":"x9","Name":"No data at all","Type":"Movie"}
                ]}
                """)
            : null);

        var result = await new JellyfinAdapter(new StubInstanceHttpClientFactory(handler)).FetchAsync(Connection);

        var row = Assert.Single(result.WatchHistory);
        Assert.Equal(("bob", "e1", 12.5, 0L), (row.UserName, row.ExternalKey, row.PercentComplete, row.TimesPlayed));
    }

    [Fact]
    public async Task FetchAsync_WhenUsersCantBeListed_StillReturnsTheLibrary()
    {
        // A key without admin rights gets 403 from /Users: no history, but the library must still load.
        var result = await new JellyfinAdapter(new StubInstanceHttpClientFactory(Server(_ => null, usersForbidden: true))).FetchAsync(Connection);

        Assert.Equal(2, result.MediaItems.Count);
        Assert.Empty(result.WatchHistory);
    }

    [Fact]
    public async Task FetchAsync_OneUsersFailure_DoesNotDropTheOthers()
    {
        var handler = Server(user => user == "u-alice"
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Json("""{"Items":[{"Id":"m1","Name":"Foo","Type":"Movie","UserData":{"PlayCount":1,"Played":true}}]}"""));

        var result = await new JellyfinAdapter(new StubInstanceHttpClientFactory(handler)).FetchAsync(Connection);

        Assert.Equal("bob", Assert.Single(result.WatchHistory).UserName);
    }
}

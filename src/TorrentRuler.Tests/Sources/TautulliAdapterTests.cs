using System.Net;
using System.Text;
using TorrentRuler.Core.Domain;
using TorrentRuler.Core.Domain.SourceData;
using TorrentRuler.Sources.Adapters;
using TorrentRuler.Tests.TestHelpers;
using Xunit;

namespace TorrentRuler.Tests.Sources;

public class TautulliAdapterTests
{
    private static readonly SourceConnectionInfo Connection = new()
    {
        InstanceId = 4,
        InstanceName = "t1",
        SourceType = SourceType.Tautulli,
        BaseUrl = "http://tautulli:8181",
        ApiKey = "key",
        TimeoutSeconds = 5,
        VerifySsl = true
    };

    [Fact]
    public async Task FetchAsync_MapsDescriptiveFieldsFromTheHistoryRow()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"response":{"data":{"data":[
                  {"rating_key":42,"full_title":"Show - Pilot","media_type":"episode","friendly_name":"alice","date":1767225600,
                   "year":2008,"original_title":"Pilote","grandparent_title":"Show","parent_media_index":1,"media_index":2,
                   "originally_available_at":"2008-01-20"}
                ]}}}
                """, Encoding.UTF8, "application/json")
        });

        var result = await new TautulliAdapter(new StubInstanceHttpClientFactory(handler)).FetchAsync(Connection);

        var row = result.WatchHistory.Single();
        Assert.Equal((2008, "Pilote", "Show", 1, 2), (row.ProductionYear, row.OriginalTitle, row.SeriesName, row.SeasonNumber, row.EpisodeNumber));
        Assert.Equal(DateTimeOffset.Parse("2008-01-20T00:00:00Z"), row.PremiereDate);
    }
}

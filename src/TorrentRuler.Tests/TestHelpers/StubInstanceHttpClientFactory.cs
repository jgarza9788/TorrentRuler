using TorrentRuler.Core.Domain.SourceData;
using TorrentRuler.Sources.Http;

namespace TorrentRuler.Tests.TestHelpers;

internal sealed class StubInstanceHttpClientFactory(HttpMessageHandler handler) : IInstanceHttpClientFactory
{
    public HttpClient CreateClient(SourceConnectionInfo connection) => new(handler, disposeHandler: false);
}

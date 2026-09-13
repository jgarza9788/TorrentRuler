using TorrentRuler.Core.Domain.SourceData;

namespace TorrentRuler.Sources.Http;

public interface IInstanceHttpClientFactory
{
    HttpClient CreateClient(SourceConnectionInfo connection);
}

/// <summary>
/// Picks between two pre-configured named HttpClients (cert validation on/off) based on
/// the instance's VerifySsl flag. Registered by AddTorrentRulerSources.
/// </summary>
public class InstanceHttpClientFactory(IHttpClientFactory httpClientFactory) : IInstanceHttpClientFactory
{
    public const string SecureClientName = "TorrentRuler.Source.Secure";
    public const string InsecureClientName = "TorrentRuler.Source.Insecure";

    public HttpClient CreateClient(SourceConnectionInfo connection) =>
        httpClientFactory.CreateClient(connection.VerifySsl ? SecureClientName : InsecureClientName);
}

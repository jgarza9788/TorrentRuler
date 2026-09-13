using Microsoft.Extensions.DependencyInjection;
using TorrentRuler.Core.Interfaces;
using TorrentRuler.Sources.Adapters;
using TorrentRuler.Sources.Cache;
using TorrentRuler.Sources.Concurrency;
using TorrentRuler.Sources.Coordination;
using TorrentRuler.Sources.Http;
using TorrentRuler.Sources.Storage;

namespace TorrentRuler.Sources;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything in this project: named HTTP clients, adapters, the cache,
    /// the per-host concurrency limiter, and the refresh coordinator. Callers must
    /// separately register an IParallelismSettingsProvider (TorrentRuler.Infrastructure owns
    /// that implementation, since it reads AppSettings from the database).
    /// </summary>
    public static IServiceCollection AddTorrentRulerSources(this IServiceCollection services)
    {
        services.AddHttpClient(InstanceHttpClientFactory.SecureClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false });

        services.AddHttpClient(InstanceHttpClientFactory.InsecureClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                UseCookies = false,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            });

        services.AddSingleton<IInstanceHttpClientFactory, InstanceHttpClientFactory>();
        services.AddSingleton<IHostConcurrencyLimiter, HostConcurrencyLimiter>();
        services.AddSingleton<ISourceDataCache, InMemorySourceDataCache>();
        services.AddSingleton<SourceCacheOptions>();
        services.AddSingleton<IStorageUsageService, StorageUsageService>();

        services.AddSingleton<QbtAdapter>();
        services.AddSingleton<ISourceAdapter>(sp => sp.GetRequiredService<QbtAdapter>());
        services.AddSingleton<IQbtTorrentFilesProvider>(sp => sp.GetRequiredService<QbtAdapter>());
        services.AddSingleton<IQbtActionClient>(sp => sp.GetRequiredService<QbtAdapter>());
        services.AddSingleton<ISourceAdapter, PlexAdapter>();
        services.AddSingleton<ISourceAdapter, JellyfinAdapter>();
        services.AddSingleton<ISourceAdapter, TautulliAdapter>();
        services.AddSingleton<ISourceAdapter, JellystatAdapter>();
        services.AddSingleton<ISourceAdapter, JellyglanceAdapter>();

        services.AddSingleton<ISourceAdapterResolver, SourceAdapterResolver>();
        services.AddSingleton<ISourceRefreshCoordinator, SourceRefreshCoordinator>();

        return services;
    }
}

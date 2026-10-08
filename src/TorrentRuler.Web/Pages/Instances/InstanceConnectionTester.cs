using Microsoft.EntityFrameworkCore;
using TorrentRuler.Core.Domain;
using TorrentRuler.Core.Domain.SourceData;
using TorrentRuler.Infrastructure.Persistence;
using TorrentRuler.Infrastructure.Security;
using TorrentRuler.Sources.Adapters;

namespace TorrentRuler.Web.Pages.Instances;

/// <summary>
/// Tests saved instances' connections and records each outcome on the instance
/// (<see cref="Instance.LastTestedAt"/> and friends), so the status survives a reload and the
/// dashboard can show it.
/// </summary>
public class InstanceConnectionTester(
    AppDbContext db,
    ISecretProtector secretProtector,
    ISourceAdapterResolver adapterResolver,
    ILogger<InstanceConnectionTester> logger)
{
    public const int MaxMessageLength = 500;
    private const int TestAllParallelism = 4;

    /// <summary>Tests one saved instance and records the result. Null if there is no such instance.</summary>
    public async Task<ConnectionTestResult?> TestAndRecordAsync(int instanceId, CancellationToken ct = default)
    {
        var instance = await db.Instances.SingleOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is null)
        {
            return null;
        }

        var result = await TestAsync(instance, ct);
        Record(instance, result);
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>Tests every enabled instance (a few at a time) and records each result.</summary>
    public async Task<(int Succeeded, int Total)> TestAllAsync(CancellationToken ct = default)
    {
        var instances = await db.Instances.Where(i => i.Enabled).ToListAsync(ct);

        // The adapter calls run in parallel; the DbContext isn't thread-safe, so results are
        // applied to the tracked entities afterwards, on this thread.
        var results = new ConnectionTestResult[instances.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, instances.Count),
            new ParallelOptions { MaxDegreeOfParallelism = TestAllParallelism, CancellationToken = ct },
            async (i, token) => results[i] = await TestAsync(instances[i], token));

        for (var i = 0; i < instances.Count; i++)
        {
            Record(instances[i], results[i]);
        }
        await db.SaveChangesAsync(ct);

        return (results.Count(r => r.Success), instances.Count);
    }

    private async Task<ConnectionTestResult> TestAsync(Instance instance, CancellationToken ct)
    {
        logger.LogInformation(
            "Connection test requested for instance {InstanceId} '{InstanceName}' ({SourceType})",
            instance.Id, instance.Name, instance.SourceType);

        ConnectionTestResult result;
        try
        {
            var connection = new SourceConnectionInfo
            {
                InstanceId = instance.Id,
                InstanceName = instance.Name,
                SourceType = instance.SourceType,
                BaseUrl = instance.BaseUrl,
                ApiKey = instance.ApiKeyProtected is null ? null : secretProtector.Unprotect(instance.ApiKeyProtected),
                Username = instance.Username,
                Password = instance.PasswordProtected is null ? null : secretProtector.Unprotect(instance.PasswordProtected),
                TimeoutSeconds = instance.TimeoutSeconds,
                VerifySsl = instance.VerifySsl,
                ExtraConfigJson = instance.ExtraConfigJson
            };
            result = await adapterResolver.Resolve(instance.SourceType).TestConnectionAsync(connection, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Undecryptable stored credentials and the like: a failed test, not a crashed page.
            result = new ConnectionTestResult { Success = false, Message = ex.Message };
        }

        if (result.Success)
        {
            logger.LogInformation("Connection test OK for '{InstanceName}': {Message}", instance.Name, result.Message);
        }
        else
        {
            logger.LogWarning("Connection test FAILED for '{InstanceName}': {Message}", instance.Name, result.Message);
        }
        return result;
    }

    private static void Record(Instance instance, ConnectionTestResult result)
    {
        instance.LastTestedAt = DateTimeOffset.UtcNow;
        instance.LastTestSucceeded = result.Success;
        instance.LastTestLatencyMs = (int)Math.Round(result.Duration.TotalMilliseconds, MidpointRounding.AwayFromZero);
        instance.LastTestMessage = result.Message is { Length: > MaxMessageLength } m ? m[..MaxMessageLength] : result.Message;
    }
}

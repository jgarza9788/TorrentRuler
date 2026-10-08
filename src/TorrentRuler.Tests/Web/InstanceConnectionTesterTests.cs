using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TorrentRuler.Core.Domain;
using TorrentRuler.Core.Domain.SourceData;
using TorrentRuler.Core.Interfaces;
using TorrentRuler.Infrastructure.Persistence;
using TorrentRuler.Infrastructure.Security;
using TorrentRuler.Sources.Adapters;
using TorrentRuler.Web.Pages.Instances;
using Xunit;

namespace TorrentRuler.Tests.Web;

/// <summary>A connection test's outcome is stored on the instance so the list and dashboard can show it later.</summary>
public class InstanceConnectionTesterTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AppDbContext _db;
    private readonly StubAdapter _adapter = new();
    private readonly InstanceConnectionTester _tester;

    public InstanceConnectionTesterTests()
    {
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _tester = new InstanceConnectionTester(_db, new PlainProtector(), new StubResolver(_adapter), NullLogger<InstanceConnectionTester>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private Instance AddInstance(string name, bool enabled = true)
    {
        var instance = new Instance { Name = name, SourceType = SourceType.Jellyfin, BaseUrl = "http://x", Enabled = enabled };
        _db.Instances.Add(instance);
        _db.SaveChanges();
        return instance;
    }

    private Instance Reload(int id) => _db.Instances.AsNoTracking().Single(i => i.Id == id);

    [Fact]
    public async Task Success_RecordsStatusLatencyAndTime()
    {
        var instance = AddInstance("jf1");
        _adapter.Next = new ConnectionTestResult { Success = true, Message = "Connected to Jellyfin.", Duration = TimeSpan.FromMilliseconds(42.4) };

        var result = await _tester.TestAndRecordAsync(instance.Id);

        Assert.True(result!.Success);
        var stored = Reload(instance.Id);
        Assert.True(stored.LastTestSucceeded);
        Assert.Equal(42, stored.LastTestLatencyMs);
        Assert.Equal("Connected to Jellyfin.", stored.LastTestMessage);
        Assert.NotNull(stored.LastTestedAt);
        Assert.InRange(stored.LastTestedAt!.Value, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task Failure_RecordsTheMessage_TruncatedTo500()
    {
        var instance = AddInstance("jf1");
        _adapter.Next = new ConnectionTestResult { Success = false, Message = new string('x', 800), Duration = TimeSpan.FromMilliseconds(5) };

        await _tester.TestAndRecordAsync(instance.Id);

        var stored = Reload(instance.Id);
        Assert.False(stored.LastTestSucceeded);
        Assert.Equal(500, stored.LastTestMessage!.Length);
    }

    [Fact]
    public async Task UnknownInstance_ReturnsNull() => Assert.Null(await _tester.TestAndRecordAsync(999));

    [Fact]
    public async Task TestAll_TestsOnlyEnabledInstances_AndCountsSuccesses()
    {
        var a = AddInstance("a");
        var b = AddInstance("b");
        var off = AddInstance("off", enabled: false);
        _adapter.Next = new ConnectionTestResult { Success = true, Duration = TimeSpan.FromMilliseconds(1) };

        var (ok, total) = await _tester.TestAllAsync();

        Assert.Equal((2, 2), (ok, total));
        Assert.NotNull(Reload(a.Id).LastTestedAt);
        Assert.NotNull(Reload(b.Id).LastTestedAt);
        Assert.Null(Reload(off.Id).LastTestedAt);
    }

    private sealed class StubAdapter : ISourceAdapter
    {
        public ConnectionTestResult Next { get; set; } = new() { Success = true };
        public SourceType SourceType => SourceType.Jellyfin;
        public Task<ConnectionTestResult> TestConnectionAsync(SourceConnectionInfo connection, CancellationToken ct = default) => Task.FromResult(Next);
        public Task<SourceFetchResult> FetchAsync(SourceConnectionInfo connection, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubResolver(ISourceAdapter adapter) : ISourceAdapterResolver
    {
        public ISourceAdapter Resolve(SourceType type) => adapter;
    }

    private sealed class PlainProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string protectedText) => protectedText;
    }
}

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TorrentRuler.Core.Domain;
using TorrentRuler.Engine;
using TorrentRuler.Infrastructure.Persistence;
using TorrentRuler.Snapshot;
using TorrentRuler.Web;
using TorrentRuler.Web.Pages.Rules;
using Xunit;

namespace TorrentRuler.Tests.Web;

/// <summary>The Rules list's bulk actions, inline priority edit, and undo-delete.</summary>
public class RulesIndexHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AppDbContext _db;
    private readonly RecordingRunner _runner = new();
    private readonly TempDataDictionary _tempData;

    public RulesIndexHandlerTests()
    {
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _tempData = new TempDataDictionary(new DefaultHttpContext(), new MemoryTempDataProvider());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // A fresh model per request, sharing TempData the way the cookie provider would across requests.
    private IndexModel Page() => new(_db, _runner)
    {
        TempData = _tempData,
        PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = new DefaultHttpContext() }
    };

    private Rule AddRule(string name, bool enabled = true)
    {
        var rule = new Rule
        {
            Name = name, Enabled = enabled, Priority = 5, CronExpression = "*/5 * * * *", TimeZoneId = "UTC",
            ConditionTreeJson = """{"kind":"group"}""", ActionsJson = "[]", TargetInstanceIdsJson = "[1]",
            UseAdvancedSql = true, AdvancedSql = "SELECT 1 AS instance_id, 'h' AS torrent_hash"
        };
        _db.Rules.Add(rule);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return rule;
    }

    private Rule? Find(int id) => _db.Rules.AsNoTracking().SingleOrDefault(r => r.Id == id);
    private string? LastToast() => Toasts.Peek(_tempData).LastOrDefault()?.Message;

    [Fact]
    public async Task Bulk_Disable_ChangesOnlyTheSelectedRules()
    {
        var a = AddRule("a");
        var b = AddRule("b");
        var c = AddRule("c");

        await Page().OnPostBulkAsync("disable", [a.Id, b.Id], default);

        Assert.False(Find(a.Id)!.Enabled);
        Assert.False(Find(b.Id)!.Enabled);
        Assert.True(Find(c.Id)!.Enabled);
        Assert.Equal("Disabled 2 rule(s).", LastToast());
    }

    [Fact]
    public async Task Bulk_Delete_RemovesThem()
    {
        var a = AddRule("a");
        var b = AddRule("b");

        await Page().OnPostBulkAsync("delete", [a.Id, b.Id], default);

        Assert.Null(Find(a.Id));
        Assert.Null(Find(b.Id));
    }

    [Fact]
    public async Task Bulk_Run_RunsEachSelectedRule()
    {
        var a = AddRule("a");
        var b = AddRule("b");

        await Page().OnPostBulkAsync("run", [a.Id, b.Id], default);

        Assert.Equal([a.Id, b.Id], _runner.Ran);
    }

    [Fact]
    public async Task SetPriority_UpdatesIt()
    {
        var a = AddRule("a");

        await Page().OnPostSetPriorityAsync(a.Id, 42, default);

        Assert.Equal(42, Find(a.Id)!.Priority);
    }

    [Fact]
    public async Task Delete_ThenRestore_BringsTheRuleBackUnderTheSameId()
    {
        var a = AddRule("a");

        await Page().OnPostDeleteAsync(a.Id, default);
        Assert.Null(Find(a.Id));
        Assert.Equal("/Rules?handler=RestoreDeleted", Toasts.Peek(_tempData).Last().UndoUrl);

        await Page().OnPostRestoreDeletedAsync(default);

        var restored = Find(a.Id);
        Assert.NotNull(restored);
        Assert.Equal(("a", "*/5 * * * *", "[1]", "SELECT 1 AS instance_id, 'h' AS torrent_hash"),
            (restored!.Name, restored.CronExpression, restored.TargetInstanceIdsJson, restored.AdvancedSql));
        Assert.Equal("Restored rule \"a\".", LastToast());
    }

    [Fact]
    public async Task Restore_Twice_SecondTimeHasNothingToUndo()
    {
        var a = AddRule("a");
        await Page().OnPostDeleteAsync(a.Id, default);
        await Page().OnPostRestoreDeletedAsync(default);

        await Page().OnPostRestoreDeletedAsync(default);

        Assert.Equal("Nothing to undo.", LastToast());
        Assert.Single(_db.Rules.AsNoTracking());
    }

    [Fact]
    public async Task TwoDeletes_RestoreBringsBackTheLatest()
    {
        var a = AddRule("a");
        var b = AddRule("b");
        await Page().OnPostDeleteAsync(a.Id, default);
        await Page().OnPostDeleteAsync(b.Id, default);

        await Page().OnPostRestoreDeletedAsync(default);

        Assert.Null(Find(a.Id));
        Assert.NotNull(Find(b.Id));
    }

    [Fact]
    public async Task Restore_WhenTheNameWasTakenSince_FailsPolitely()
    {
        var a = AddRule("a");
        await Page().OnPostDeleteAsync(a.Id, default);
        AddRule("a");

        await Page().OnPostRestoreDeletedAsync(default);

        Assert.Equal("Can't restore \"a\": another rule now has that name.", LastToast());
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class RecordingRunner : IRuleRunner
    {
        public List<int> Ran { get; } = [];
        public Task RunAsync(int ruleId, CancellationToken ct = default) { Ran.Add(ruleId); return Task.CompletedTask; }
        public Task<RulePreview> DryRunAsync(RuleDraft draft, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SnapshotDatabase> BuildSandboxSnapshotAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}

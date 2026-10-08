using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TorrentRuler.Core.Domain;
using TorrentRuler.Engine;
using TorrentRuler.Engine.Conditions;
using TorrentRuler.Engine.Conditions.AdvancedSql;
using TorrentRuler.Infrastructure.Persistence;
using TorrentRuler.Snapshot;
using TorrentRuler.Web.Pages.Rules;
using Xunit;

namespace TorrentRuler.Tests.Web;

/// <summary>Saving a rule from the editor.</summary>
public class RuleEditSaveTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AppDbContext _db;

    public RuleEditSaveTests()
    {
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated(); // seeds the AppSettings row
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private EditModel Page(EditModel.InputModel input) => new(_db, new ConditionSqlCompiler(), new AdvancedSqlExecutor(), new LenientFields(), new NoRunner())
    {
        Input = input,
        PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
        TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempData())
    };

    [Fact]
    public async Task Save_WithNoTargetInstances_MeansAllInstances()
    {
        var input = new EditModel.InputModel
        {
            Name = "All of them",
            CronExpression = "*/15 * * * *",
            TimeZoneId = "UTC",
            UseAdvancedSql = true,
            AdvancedSql = AdvancedSqlTemplate.NewRule,
            ActionsJson = """[{"type":"add_tags","Tags":["x"]}]""",
            TargetInstanceIds = []
        };

        var result = await Page(input).OnPostSaveAsync(default);

        Assert.IsType<RedirectToPageResult>(result);
        var saved = _db.Rules.AsNoTracking().Single();
        Assert.Equal("[]", saved.TargetInstanceIdsJson);
    }

    private sealed class LenientFields : IFieldContextProvider
    {
        public Task<FieldResolutionContext> GetAsync(CancellationToken ct = default) => Task.FromResult(FieldResolutionContext.Lenient);
    }

    private sealed class NoRunner : IRuleRunner
    {
        public Task RunAsync(int ruleId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RulePreview> DryRunAsync(RuleDraft draft, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SnapshotDatabase> BuildSandboxSnapshotAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NullTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}

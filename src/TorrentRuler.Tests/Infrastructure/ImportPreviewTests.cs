using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TorrentRuler.Core.Domain;
using TorrentRuler.Infrastructure.Config;
using TorrentRuler.Infrastructure.Persistence;
using Xunit;

namespace TorrentRuler.Tests.Infrastructure;

/// <summary>Import shows what would change before anything is written.</summary>
public class ImportPreviewTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AppDbContext _db;
    private readonly ConfigPortabilityService _service;

    public ImportPreviewTests()
    {
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _service = new ConfigPortabilityService(_db);

        _db.Rules.Add(new Rule
        {
            Name = "Tag old", CronExpression = "0 4 * * *", ConditionTreeJson = "{}", ActionsJson = "[]", TargetInstanceIdsJson = "[]"
        });
        _db.Instances.Add(new Instance { Name = "qbt1", SourceType = SourceType.Qbittorrent, BaseUrl = "http://qbt:8080" });
        _db.StoragePaths.Add(new StoragePathConfig { Name = "downloads", Path = "/downloads" });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Theory, InlineData(ConfigFormat.Json), InlineData(ConfigFormat.Yaml)]
    public async Task ReimportingAnExport_ChangesNothing(ConfigFormat format)
    {
        var rules = await _service.PreviewImportAsync(await _service.ExportRulesAsync(format), format, "rules");
        var config = await _service.PreviewImportAsync(await _service.ExportConfigAsync(format), format, "config");

        Assert.Null(rules.Error);
        Assert.Null(config.Error);
        Assert.All(rules.Changes.Concat(config.Changes), c => Assert.Equal(ImportChangeKind.Unchanged, c.Kind));
        Assert.Contains(config.Changes, c => c.Section == "Instance" && c.Name == "qbt1");
        Assert.Contains(config.Changes, c => c.Section == "StoragePath" && c.Name == "downloads");
        Assert.Contains(config.Changes, c => c.Section == "Settings");
    }

    [Fact]
    public async Task NewRule_IsAnAdd_ChangedCron_IsAnUpdateNamingTheField()
    {
        var json = (await _service.ExportRulesAsync(ConfigFormat.Json))
            .Replace("\"0 4 * * *\"", "\"0 5 * * *\"")
            .Replace("\"Rules\": [", """
                "Rules": [{"Name":"Brand new","CronExpression":"*/15 * * * *","ConditionTreeJson":"{}","ActionsJson":"[]","TargetInstanceIdsJson":"[]"},
                """);

        var preview = await _service.PreviewImportAsync(json, ConfigFormat.Json, "rules");

        Assert.Null(preview.Error);
        Assert.Equal(ImportChangeKind.Add, preview.Changes.Single(c => c.Name == "Brand new").Kind);
        var update = preview.Changes.Single(c => c.Name == "Tag old");
        Assert.Equal(ImportChangeKind.Update, update.Kind);
        Assert.Equal(["CronExpression"], update.ChangedFields);
    }

    [Fact]
    public async Task MalformedContent_ReportsAnError_WithoutThrowing()
    {
        var preview = await _service.PreviewImportAsync("Rules: [ {{ not yaml", ConfigFormat.Yaml, "rules");

        Assert.NotNull(preview.Error);
        Assert.Empty(preview.Changes);
    }

    [Fact]
    public async Task Preview_NeverWrites()
    {
        var before = _db.Rules.AsNoTracking().Single();
        var json = (await _service.ExportRulesAsync(ConfigFormat.Json)).Replace("\"0 4 * * *\"", "\"0 5 * * *\"");

        await _service.PreviewImportAsync(json, ConfigFormat.Json, "rules");

        var after = _db.Rules.AsNoTracking().Single();
        Assert.Equal((before.CronExpression, before.UpdatedAt), (after.CronExpression, after.UpdatedAt));
        Assert.Equal(1, _db.Rules.Count());
    }
}

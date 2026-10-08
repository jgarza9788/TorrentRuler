using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TorrentRuler.Core.Domain;
using TorrentRuler.Infrastructure.Config;
using TorrentRuler.Infrastructure.Persistence;
using Xunit;

namespace TorrentRuler.Tests.Infrastructure;

/// <summary>Rule files exported before advanced SQL became a full query still import; new exports use only the new field.</summary>
public class RuleConfigCompatTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AppDbContext _db;
    private readonly ConfigPortabilityService _service;

    public RuleConfigCompatTests()
    {
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _service = new ConfigPortabilityService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static string RuleFile(ConfigFormat format, string sqlFields) => format == ConfigFormat.Json
        ? $$"""
            {"Rules":[{"Name":"R","CronExpression":"* * * * *","ConditionTreeJson":"{}","ActionsJson":"[]",
                       "TargetInstanceIdsJson":"[]","UseAdvancedSql":true,{{sqlFields}}}]}
            """
        : $"""
            Rules:
            - Name: R
              CronExpression: '* * * * *'
              ConditionTreeJson: '{"{}"}'
              ActionsJson: '[]'
              TargetInstanceIdsJson: '[]'
              UseAdvancedSql: true
            {sqlFields}
            """;

    private static string Field(ConfigFormat format, string key, string value) => format == ConfigFormat.Json
        ? $"\"{key}\":\"{value}\""
        : $"  {key}: '{value}'";

    private string? StoredSql() => _db.Rules.AsNoTracking().Single(r => r.Name == "R").AdvancedSql;

    [Theory, InlineData(ConfigFormat.Json), InlineData(ConfigFormat.Yaml)]
    public async Task Import_LegacyWhere_IsConvertedToFullQuery(ConfigFormat format)
    {
        await _service.ImportRulesAsync(RuleFile(format, Field(format, "AdvancedSqlWhere", "t.ratio > 2")), format);

        Assert.Equal(AdvancedSqlTemplate.FromLegacyWhere("t.ratio > 2"), StoredSql());
    }

    [Theory, InlineData(ConfigFormat.Json), InlineData(ConfigFormat.Yaml)]
    public async Task Import_NewFieldWins_WhenBothPresent(ConfigFormat format)
    {
        var both = format == ConfigFormat.Json
            ? $"{Field(format, "AdvancedSql", "SELECT 1 AS instance_id, 'h' AS torrent_hash")},{Field(format, "AdvancedSqlWhere", "t.ratio > 2")}"
            : $"{Field(format, "AdvancedSql", "SELECT 1 AS instance_id, ''h'' AS torrent_hash")}\n{Field(format, "AdvancedSqlWhere", "t.ratio > 2")}";

        await _service.ImportRulesAsync(RuleFile(format, both), format);

        Assert.Equal("SELECT 1 AS instance_id, 'h' AS torrent_hash", StoredSql());
    }

    [Theory, InlineData(ConfigFormat.Json), InlineData(ConfigFormat.Yaml)]
    public async Task Export_WritesAdvancedSql_AndNeverTheLegacyKey(ConfigFormat format)
    {
        _db.Rules.Add(new Rule
        {
            Name = "R", CronExpression = "* * * * *", ConditionTreeJson = "{}", ActionsJson = "[]", TargetInstanceIdsJson = "[]",
            UseAdvancedSql = true, AdvancedSql = AdvancedSqlTemplate.NewRule
        });
        await _db.SaveChangesAsync();

        var exported = await _service.ExportRulesAsync(format);

        Assert.Contains("AdvancedSql", exported);
        Assert.DoesNotContain("AdvancedSqlWhere", exported, StringComparison.OrdinalIgnoreCase);
    }

    [Theory, InlineData(ConfigFormat.Json), InlineData(ConfigFormat.Yaml)]
    public async Task ExportThenImport_RoundTripsTheQueryUnchanged(ConfigFormat format)
    {
        _db.Rules.Add(new Rule
        {
            Name = "R", CronExpression = "* * * * *", ConditionTreeJson = "{}", ActionsJson = "[]", TargetInstanceIdsJson = "[]",
            UseAdvancedSql = true, AdvancedSql = AdvancedSqlTemplate.NewRule
        });
        await _db.SaveChangesAsync();
        var exported = await _service.ExportRulesAsync(format);

        await _service.ImportRulesAsync(exported, format);

        Assert.Equal(AdvancedSqlTemplate.NewRule, StoredSql());
    }
}

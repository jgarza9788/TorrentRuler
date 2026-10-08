using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TorrentRuler.Infrastructure.Persistence;
using Xunit;

namespace TorrentRuler.Tests.Infrastructure;

/// <summary>Upgrading a database written before advanced SQL became a full query keeps every rule working.</summary>
public class AdvancedSqlMigrationTests : IDisposable
{
    private const string PreviousMigration = "20260907190021_RemoveStreamystatsInstances";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"torrentruler-migration-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    [Fact]
    public void Migrate_ConvertsLegacyWhereClauses_AndLeavesBasicRulesAlone()
    {
        using (var db = NewContext())
        {
            db.GetService<IMigrator>().Migrate(PreviousMigration);
            db.Database.ExecuteSqlRaw("""
                INSERT INTO Rules (Name, Enabled, Priority, StopOnMatch, DryRun, CronExpression, TimeZoneId, ConditionTreeJson,
                                   AdvancedSqlWhere, UseAdvancedSql, ActionsJson, TargetInstanceIdsJson, CreatedAt, UpdatedAt)
                VALUES ('A', 1, 0, 0, 0, '* * * * *', 'UTC', '{{}}', 't.ratio > 2', 1, '[]', '[]', '2026-01-01', '2026-01-01'),
                       ('B', 1, 0, 0, 0, '* * * * *', 'UTC', '{{}}', '   ',         0, '[]', '[]', '2026-01-01', '2026-01-01'),
                       ('C', 1, 0, 0, 0, '* * * * *', 'UTC', '{{}}', NULL,          0, '[]', '[]', '2026-01-01', '2026-01-01');
                """);
        }

        using (var db = NewContext())
        {
            db.Database.Migrate();
        }

        var rows = ReadRules();
        Assert.Equal(("SELECT DISTINCT t.instance_id AS instance_id, t.hash AS torrent_hash\nFROM qbittorrent t\nWHERE t.ratio > 2", true), rows["A"]);
        Assert.Equal(("   ", false), rows["B"]);
        Assert.Equal(((string?)null, false), rows["C"]);
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_path}").Options);

    private Dictionary<string, (string? Sql, bool UseAdvancedSql)> ReadRules()
    {
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, AdvancedSql, UseAdvancedSql FROM Rules";
        using var reader = command.ExecuteReader();
        var rows = new Dictionary<string, (string?, bool)>();
        while (reader.Read())
        {
            rows[reader.GetString(0)] = (reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetBoolean(2));
        }
        return rows;
    }
}

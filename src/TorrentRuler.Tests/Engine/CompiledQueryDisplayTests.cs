using System.Text.Json;
using TorrentRuler.Core.Domain.Conditions;
using TorrentRuler.Core.Domain.SourceData;
using TorrentRuler.Engine.Conditions;
using TorrentRuler.Engine.Conditions.AdvancedSql;
using TorrentRuler.Snapshot;
using Xunit;

namespace TorrentRuler.Tests.Engine;

/// <summary>Basic mode's "switch to SQL" shows the compiled query with its parameters written in as literals.</summary>
public class CompiledQueryDisplayTests
{
    [Fact]
    public void ToDisplaySql_ReplacesLongerParameterNamesFirst()
    {
        var parameters = Enumerable.Range(0, 11).ToDictionary(i => $"$p{i}", i => (object)$"v{i}");
        var query = new CompiledQuery { Sql = "x = $p1 OR x = $p10", Parameters = parameters };

        Assert.Equal("x = 'v1' OR x = 'v10'", query.ToDisplaySql());
    }

    [Fact]
    public void ToDisplaySql_WritesLiteralsForEachValueType()
    {
        var query = new CompiledQuery
        {
            Sql = "$p0 $p1 $p2 $p3 $p4",
            Parameters = new Dictionary<string, object>
            {
                ["$p0"] = "O'Brien", ["$p1"] = 42L, ["$p2"] = 1.5, ["$p3"] = true, ["$p4"] = DBNull.Value
            }
        };

        Assert.Equal("'O''Brien' 42 1.5 1 NULL", query.ToDisplaySql());
    }

    [Fact]
    public async Task ToDisplaySql_RunsAsAdvancedSql_AndMatchesTheSameTorrents()
    {
        using var db = new SnapshotDatabase();
        db.Rebuild(new SnapshotInput
        {
            Torrents = [.. new[] { "linux", "O'Brien", "tv", "c3" }.Select((c, i) =>
                new TorrentRecord { InstanceId = 1, InstanceName = "qbt", Hash = $"h{i}", Name = $"T{i}", Category = c, SizeBytes = 1, Progress = 1 })]
        });

        // Twelve alternatives so the parameter list reaches $p10 and $p11.
        var categories = new[] { "linux", "O'Brien", "a", "b", "c", "d", "e", "f", "g", "h", "i", "c3" };
        var tree = new GroupNode
        {
            Operator = LogicalOperator.Or,
            Children = [.. categories.Select(c => (ConditionNode)new ComparisonNode
            {
                Field = "qbittorrent.*.category", Operator = ComparisonOperator.Eq, Value = JsonSerializer.SerializeToElement(c)
            })]
        };

        var compiler = new ConditionSqlCompiler();
        var compiled = compiler.Compile(tree);
        var expected = (await compiler.ExecuteAsync(db, compiled)).Select(m => m.TorrentHash).Order().ToList();

        var executor = new AdvancedSqlExecutor();
        var validation = executor.Validate(db, compiled.ToDisplaySql());
        Assert.True(validation.IsValid, validation.ErrorMessage);
        var actual = (await executor.ExecuteAsync(db, validation.CompiledSql!)).Select(m => m.TorrentHash).Order().ToList();

        Assert.Equal(["h0", "h1", "h3"], expected);
        Assert.Equal(expected, actual);
    }
}

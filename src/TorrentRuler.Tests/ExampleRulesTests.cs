using System.Text.Json;
using TorrentRuler.Core.Domain.Actions;
using TorrentRuler.Core.Domain.Conditions;
using TorrentRuler.Engine.Conditions;
using TorrentRuler.Engine.Scheduling;
using TorrentRuler.Infrastructure.Config;
using Xunit;

namespace TorrentRuler.Tests;

/// <summary>Verifies examples/example-rules.json (the bundled example library, importable from Settings) is well-formed -- every rule actually parses, compiles, and validates, the same way a real import would.</summary>
public class ExampleRulesTests
{
    private static string LoadJson() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "example-rules.json"));

    [Fact]
    public void ExampleRules_AllParseCompileAndValidate()
    {
        var dto = JsonSerializer.Deserialize<RulesExportDto>(LoadJson());
        Assert.NotNull(dto);
        Assert.InRange(dto!.Rules.Count, 5, 10);

        var compiler = new ConditionSqlCompiler();
        foreach (var rule in dto.Rules)
        {
            Assert.False(rule.UseAdvancedSql, $"{rule.Name}: examples should use the visual condition tree, not advanced SQL.");

            var tree = JsonSerializer.Deserialize<ConditionNode>(rule.ConditionTreeJson);
            Assert.NotNull(tree);
            var compiled = compiler.Compile(tree!);
            Assert.False(string.IsNullOrWhiteSpace(compiled.Sql));

            var actions = JsonSerializer.Deserialize<List<ActionDefinition>>(rule.ActionsJson);
            Assert.NotNull(actions);
            Assert.NotEmpty(actions!);

            var targetIds = JsonSerializer.Deserialize<List<int>>(rule.TargetInstanceIdsJson);
            Assert.NotNull(targetIds);

            Assert.True(rule.DryRun, $"{rule.Name}: example rules must ship with DryRun=true so importing them never immediately acts on real torrents.");

            var cronResult = CronValidator.Validate(rule.CronExpression, rule.TimeZoneId);
            Assert.True(cronResult.IsValid, $"{rule.Name}: {cronResult.ErrorMessage}");
        }
    }

    [Fact]
    public void ExampleRules_HaveUniqueNames()
    {
        var dto = JsonSerializer.Deserialize<RulesExportDto>(LoadJson());
        var names = dto!.Rules.Select(r => r.Name).ToList();
        Assert.Equal(names.Distinct().Count(), names.Count);
    }
}

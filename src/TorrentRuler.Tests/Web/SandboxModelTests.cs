using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TorrentRuler.Engine;
using TorrentRuler.Snapshot;
using TorrentRuler.Web.Pages.Sandbox;
using Xunit;

namespace TorrentRuler.Tests.Web;

public class SandboxModelTests
{
    private sealed class SnapshotRunner : IRuleRunner
    {
        public Task RunAsync(int ruleId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RulePreview> DryRunAsync(RuleDraft draft, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SnapshotDatabase> BuildSandboxSnapshotAsync(CancellationToken ct = default) => Task.FromResult(new SnapshotDatabase());
    }

    private static IndexModel Page() => new(new SnapshotRunner()) { PageContext = new PageContext { HttpContext = new DefaultHttpContext() } };

    [Fact]
    public async Task Tables_ListColumnsWithTheirTypes()
    {
        var page = Page();

        await page.OnGetAsync(null, default);

        var qbt = page.Tables.Single(t => t.Name == "qbittorrent");
        Assert.Contains(new IndexModel.ColumnInfo("size_bytes", "INTEGER"), qbt.Columns);
        Assert.Contains(new IndexModel.ColumnInfo("name", "TEXT"), qbt.Columns);
    }

    [Fact]
    public async Task Run_ExposesColumnsAndRowsForTheResultsGrid()
    {
        var page = Page();
        page.Sql = "SELECT 1 AS a, 'x' AS b";

        await page.OnPostAsync(default);

        Assert.Null(page.Error);
        Assert.Equal(["a", "b"], page.Columns);
        Assert.Equal([1L, "x"], page.Rows.Single());
    }
}

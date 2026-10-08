using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TorrentRuler.Engine;
using TorrentRuler.Engine.Conditions;

namespace TorrentRuler.Web.Pages.Sandbox;

public class IndexModel(IRuleRunner ruleRunner) : PageModel
{
    public const int MaxRows = 1000;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static readonly Regex AllowedStart = new(@"^\s*(SELECT|WITH|EXPLAIN)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [BindProperty]
    public string Sql { get; set; } = "SELECT * FROM qbittorrent LIMIT 20";

    public List<TableInfo> Tables { get; private set; } = [];

    /// <summary>The snapshot's user-defined SQL functions -- the same list the rule editor's Helpers tab shows.</summary>
    public IReadOnlyList<(string Signature, string Description)> Helpers => SourceFieldCatalog.Helpers;
    public List<string> Columns { get; private set; } = [];
    public List<object?[]> Rows { get; private set; } = [];
    public bool Truncated { get; private set; }
    public string? Error { get; private set; }
    public long ElapsedMs { get; private set; }
    public bool Ran { get; private set; }

    public record TableInfo(string Name, int RowCount, List<string> Columns);

    public async Task OnGetAsync(CancellationToken ct) => await RunAsync(execute: false, ct);

    public async Task OnPostAsync(CancellationToken ct) => await RunAsync(execute: true, ct);

    private async Task RunAsync(bool execute, CancellationToken ct)
    {
        try
        {
            using var snapshot = await ruleRunner.BuildSandboxSnapshotAsync(ct);
            using var conn = snapshot.OpenReadOnlyConnection();

            var names = new List<string>();
            using (var list = conn.CreateCommand())
            {
                list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
                using var r = list.ExecuteReader();
                while (r.Read())
                {
                    names.Add(r.GetString(0));
                }
            }

            foreach (var name in names)
            {
                var colNames = new List<string>();
                using (var cols = conn.CreateCommand())
                {
                    cols.CommandText = $"SELECT name FROM pragma_table_info('{name.Replace("'", "''")}')";
                    using var r = cols.ExecuteReader();
                    while (r.Read())
                    {
                        colNames.Add(r.GetString(0));
                    }
                }

                using var count = conn.CreateCommand();
                count.CommandText = $"SELECT COUNT(*) FROM \"{name}\"";
                Tables.Add(new TableInfo(name, Convert.ToInt32(count.ExecuteScalar()), colNames));
            }

            if (!execute)
            {
                return;
            }

            Ran = true;
            var sql = (Sql ?? "").Trim().TrimEnd(';');
            if (sql.Length == 0 || sql.Contains(';') || !AllowedStart.IsMatch(sql))
            {
                Error = "Enter a single SELECT / WITH / EXPLAIN statement.";
                return;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            var sw = Stopwatch.StartNew();

            using var command = conn.CreateCommand();
            command.CommandText = sql;
            using var reader = await command.ExecuteReaderAsync(cts.Token);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                Columns.Add(reader.GetName(i));
            }

            while (await reader.ReadAsync(cts.Token))
            {
                if (Rows.Count >= MaxRows)
                {
                    Truncated = true;
                    break;
                }

                var row = new object?[reader.FieldCount];
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                Rows.Add(row);
            }

            ElapsedMs = sw.ElapsedMilliseconds;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Error = $"Query timed out after {Timeout.TotalSeconds:0} seconds.";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
}

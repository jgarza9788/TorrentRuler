using System.Globalization;

namespace TorrentRuler.Engine.Conditions;

public class CompiledQuery
{
    public required string Sql { get; init; }
    public required IReadOnlyDictionary<string, object> Parameters { get; init; }

    /// <summary>
    /// <see cref="Sql"/> with every parameter written in as a SQL literal -- what the rule editor shows
    /// when a basic-builder condition is switched to SQL, so it can be edited and run as advanced SQL.
    /// For display and hand-editing only; the engine itself always binds parameters.
    /// </summary>
    public string ToDisplaySql()
    {
        var sql = Sql;
        // Longest names first, so "$p1" never rewrites the front of "$p10".
        foreach (var (name, value) in Parameters.OrderByDescending(p => p.Key.Length))
        {
            sql = sql.Replace(name, Literal(value), StringComparison.Ordinal);
        }
        return sql;
    }

    private static string Literal(object? value) => value switch
    {
        null or DBNull => "NULL",
        bool b => b ? "1" : "0",
        string s => Quote(s),
        sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
            => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        IFormattable f => Quote(f.ToString(null, CultureInfo.InvariantCulture)),
        _ => Quote(value.ToString() ?? "")
    };

    private static string Quote(string s) => $"'{s.Replace("'", "''")}'";
}

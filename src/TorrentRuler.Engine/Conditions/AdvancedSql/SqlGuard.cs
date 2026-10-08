using System.Text;
using System.Text.RegularExpressions;

namespace TorrentRuler.Engine.Conditions.AdvancedSql;

/// <summary>
/// The shape check every author-written query passes before it reaches SQLite: one statement,
/// SELECT or WITH … SELECT (plus EXPLAIN for the sandbox), and none of the write/schema keywords.
/// Shared by rule conditions and the SQL sandbox.
///
/// Keywords are matched only in the SQL itself, never inside string literals, quoted identifiers or
/// comments -- otherwise <c>t.name LIKE '%update%'</c> would be rejected. This is belt-and-braces:
/// both callers also run on a <c>PRAGMA query_only</c> connection, which is what actually stops a write.
/// </summary>
public static class SqlGuard
{
    private static readonly Regex ForbiddenKeyword = new(
        @"\b(ATTACH|DETACH|PRAGMA|VACUUM|INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|REINDEX)\b|\b(REPLACE)\s+INTO\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AllowedStart = new(@"^(SELECT|WITH)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AllowedStartWithExplain = new(@"^(SELECT|WITH|EXPLAIN)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Null when <paramref name="sql"/> is acceptable, otherwise a message for the author. One trailing ';' is tolerated.</summary>
    public static string? Check(string sql, bool allowExplain = false)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return "SQL cannot be empty.";
        }

        var code = MaskLiteralsAndComments(sql).Trim();
        if (code.EndsWith(';'))
        {
            code = code[..^1].TrimEnd();
        }

        if (code.Length == 0)
        {
            return "SQL cannot be empty.";
        }
        if (code.Contains(';'))
        {
            return "Only a single statement is allowed.";
        }
        if (!(allowExplain ? AllowedStartWithExplain : AllowedStart).IsMatch(code))
        {
            return "Only SELECT or WITH … SELECT queries are allowed.";
        }

        var match = ForbiddenKeyword.Match(code);
        if (match.Success)
        {
            var keyword = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            return $"'{keyword.ToUpperInvariant()}' is not allowed.";
        }

        return null;
    }

    /// <summary>
    /// <paramref name="sql"/> trimmed, without its statement-ending ';' -- including one followed only
    /// by a comment (<c>SELECT 1; -- done</c>), which would otherwise land inside the engine's wrapper.
    /// </summary>
    internal static string StripTrailingSemicolon(string sql)
    {
        var masked = MaskLiteralsAndComments(sql);
        var last = masked.Length - 1;
        while (last >= 0 && char.IsWhiteSpace(masked[last]))
        {
            last--;
        }

        return (last >= 0 && masked[last] == ';' ? sql.Remove(last, 1) : sql).Trim();
    }

    /// <summary>
    /// <paramref name="sql"/> with every string literal, quoted/bracketed identifier and comment
    /// replaced by spaces of the same length, so positions still line up with the original.
    /// </summary>
    internal static string MaskLiteralsAndComments(string sql)
    {
        var sb = new StringBuilder(sql);
        var i = 0;
        var n = sql.Length;

        while (i < n)
        {
            var c = sql[i];
            int end;

            if (c == '\'')
            {
                end = i + 1;
                while (end < n)
                {
                    if (sql[end] == '\'')
                    {
                        if (end + 1 < n && sql[end + 1] == '\'')
                        {
                            end += 2;
                            continue;
                        }
                        end++;
                        break;
                    }
                    end++;
                }
            }
            else if (c == '"' || c == '[' || c == '`')
            {
                var close = c == '[' ? ']' : c;
                end = sql.IndexOf(close, i + 1);
                end = end < 0 ? n : end + 1;
            }
            else if (c == '-' && i + 1 < n && sql[i + 1] == '-')
            {
                end = sql.IndexOf('\n', i);
                end = end < 0 ? n : end;
            }
            else if (c == '/' && i + 1 < n && sql[i + 1] == '*')
            {
                end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? n : end + 2;
            }
            else
            {
                i++;
                continue;
            }

            for (var k = i; k < end; k++)
            {
                sb[k] = ' ';
            }
            i = end;
        }

        return sb.ToString();
    }
}

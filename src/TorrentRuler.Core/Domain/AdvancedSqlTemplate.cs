namespace TorrentRuler.Core.Domain;

/// <summary>
/// The starting point for a rule's advanced SQL, and the conversion of a pre-full-query rule
/// (which stored only the expression after WHERE) into a full query. Lives in Core because both
/// the engine and config import (Infrastructure, which can't see the engine) need it.
/// </summary>
public static class AdvancedSqlTemplate
{
    /// <summary>Selects the two required columns from the torrent table, aliased <c>t</c> -- the alias field keys expand against.</summary>
    public const string Header = "SELECT DISTINCT t.instance_id AS instance_id, t.hash AS torrent_hash\nFROM qbittorrent t\nWHERE";

    /// <summary>What a new rule's SQL box starts with: valid as-is, matching every torrent until edited.</summary>
    public const string NewRule = Header + " 1 = 1";

    /// <summary>A legacy WHERE expression as a full query. Blank input comes back unchanged.</summary>
    public static string? FromLegacyWhere(string? where) =>
        string.IsNullOrWhiteSpace(where) ? where : $"{Header} {where.Trim()}";
}

using TorrentRuler.Engine.Conditions;
using TorrentRuler.Snapshot;

namespace TorrentRuler.Engine;

/// <summary>One matched torrent as a dry run lists it.</summary>
public sealed record PreviewTorrent(string InstanceName, string Hash, string Name, long SizeBytes, IReadOnlyList<string> Tags);

/// <summary>Looks matched (instance, hash) pairs up in the snapshot so a dry run can show names, sizes and tags.</summary>
public static class PreviewTorrentLookup
{
    /// <summary>The first <paramref name="max"/> matches that exist in the snapshot, in match order.</summary>
    public static List<PreviewTorrent> Describe(SnapshotDatabase snapshot, IReadOnlyList<MatchedTorrent> matches, int max)
    {
        using var connection = snapshot.OpenReadOnlyConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT instance, name, size_bytes, tags FROM qbittorrent WHERE instance_id = $id AND hash = $hash";
        var idParam = command.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Integer);
        var hashParam = command.Parameters.Add("$hash", Microsoft.Data.Sqlite.SqliteType.Text);

        var result = new List<PreviewTorrent>();
        foreach (var match in matches)
        {
            if (result.Count >= max)
            {
                break;
            }

            idParam.Value = match.InstanceId;
            hashParam.Value = match.TorrentHash;
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                continue;
            }

            var tags = reader.IsDBNull(3)
                ? []
                : reader.GetString(3).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            result.Add(new PreviewTorrent(reader.GetString(0), match.TorrentHash, reader.GetString(1), reader.GetInt64(2), tags));
        }
        return result;
    }
}

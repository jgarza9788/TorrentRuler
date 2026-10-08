namespace TorrentRuler.Core.Domain.SourceData;

/// <summary>One playback/watch event as reported by Tautulli, Jellystat, or Jellyglance.</summary>
public class WatchHistoryRecord
{
    public required int InstanceId { get; init; }
    public required string InstanceName { get; init; }

    /// <summary>Which kind of source produced this event -- decides which snapshot table it lands in.</summary>
    public required SourceType SourceType { get; init; }

    /// <summary>
    /// The media server's id for the watched item (Jellystat: the Jellyfin item id; Tautulli: the
    /// Plex rating key). Matches <see cref="MediaItemRecord.ExternalKey"/> of the same item, which is
    /// how a history row with no path of its own is linked to a library file.
    /// </summary>
    public string? ExternalKey { get; init; }

    public string? MediaTitle { get; init; }

    /// <summary>The kind of item watched, as the source names it ("Movie", "Episode").</summary>
    public string? MediaType { get; init; }

    /// <summary>The item's genres as one comma-separated string ("Drama,Sci-Fi"), or null when unknown.</summary>
    public string? Genres { get; init; }

    /// <summary>How long the item has been played in total, in the source's own unit (Jellystat's <c>time_played</c>).</summary>
    public double? TimePlayed { get; init; }

    public string? FilePath { get; init; }
    public string? UserName { get; init; }
    public DateTimeOffset? WatchedAt { get; init; }
    public double? PercentComplete { get; init; }
}

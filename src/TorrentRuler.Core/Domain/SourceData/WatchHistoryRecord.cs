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

    /// <summary>The community (audience) rating the source reports, e.g. 7.4 out of 10.</summary>
    public double? CommunityRating { get; init; }

    /// <summary>The critic rating the source reports, e.g. 85 out of 100.</summary>
    public double? CriticRating { get; init; }

    /// <summary>The content rating the source reports, e.g. "PG-13" or "TV-MA".</summary>
    public string? OfficialRating { get; init; }

    /// <summary>The year the item was produced or released.</summary>
    public int? ProductionYear { get; init; }

    /// <summary>The item's synopsis.</summary>
    public string? Overview { get; init; }

    /// <summary>The title the source sorts by (no leading "The"), e.g. "Matrix, The".</summary>
    public string? SortName { get; init; }

    /// <summary>The title in its original language.</summary>
    public string? OriginalTitle { get; init; }

    /// <summary>For an episode, the name of its series.</summary>
    public string? SeriesName { get; init; }

    /// <summary>For an episode, its season number.</summary>
    public int? SeasonNumber { get; init; }

    /// <summary>For an episode, its number within the season.</summary>
    public int? EpisodeNumber { get; init; }

    /// <summary>How long the item runs, in minutes.</summary>
    public double? RuntimeMinutes { get; init; }

    /// <summary>When the item first aired or was released.</summary>
    public DateTimeOffset? PremiereDate { get; init; }

    /// <summary>The item's studios, comma-separated.</summary>
    public string? Studios { get; init; }

    /// <summary>How many times the item has been played in total (Jellystat's <c>times_played</c>).</summary>
    public long? TimesPlayed { get; init; }

    public string? FilePath { get; init; }
    public string? UserName { get; init; }
    public DateTimeOffset? WatchedAt { get; init; }
    public double? PercentComplete { get; init; }
}

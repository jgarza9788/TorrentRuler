namespace TorrentRuler.Core.Domain.SourceData;

/// <summary>One media library item (movie or episode) as reported by Plex or Jellyfin.</summary>
public class MediaItemRecord
{
    public required int InstanceId { get; init; }
    public required string InstanceName { get; init; }
    public required SourceType SourceType { get; init; }
    public required string ExternalKey { get; init; }
    public required string Title { get; init; }
    public string? MediaType { get; init; }

    /// <summary>The item's genres as one comma-separated string ("Drama,Sci-Fi"), or null when it has none.</summary>
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

    public List<string> FilePaths { get; init; } = [];
    public DateTimeOffset? AddedAt { get; init; }
}

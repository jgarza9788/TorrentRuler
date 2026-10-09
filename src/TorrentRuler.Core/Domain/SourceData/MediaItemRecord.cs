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

    public List<string> FilePaths { get; init; } = [];
    public DateTimeOffset? AddedAt { get; init; }
}

using TorrentRuler.Core.Domain;
using TorrentRuler.Core.Domain.SourceData;

namespace TorrentRuler.Sources.Storage;

public interface IStorageUsageService
{
    /// <summary>Never throws -- an unreachable/missing path comes back as Available=false with an Error message.</summary>
    StorageUsageRecord GetUsage(StoragePathConfig config);

    /// <summary>
    /// Returns the cached recursive folder size if still within FolderSizeScanIntervalMinutes, otherwise
    /// recomputes it. <paramref name="force"/> recomputes regardless (the Instances page's "Scan now").
    /// </summary>
    Task<long?> GetOrComputeFolderSizeAsync(StoragePathConfig config, CancellationToken ct = default, bool force = false);
}

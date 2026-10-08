using TorrentRuler.Core.Domain.SourceData;
using TorrentRuler.Engine;
using TorrentRuler.Engine.Conditions;
using TorrentRuler.Snapshot;
using Xunit;

namespace TorrentRuler.Tests.Engine;

/// <summary>A dry run lists matched torrents by name, size and tags, not bare hashes.</summary>
public class PreviewTorrentLookupTests : IDisposable
{
    private readonly SnapshotDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Describe_ReadsNameSizeTagsAndInstance_InMatchOrder()
    {
        _db.Rebuild(new SnapshotInput
        {
            Torrents =
            [
                new TorrentRecord { InstanceId = 1, InstanceName = "qbt1", Hash = "aaa", Name = "Ubuntu", SizeBytes = 5_000, Progress = 1, Tags = ["iso", "linux"] },
                new TorrentRecord { InstanceId = 2, InstanceName = "qbt2", Hash = "bbb", Name = "Debian", SizeBytes = 7_000, Progress = 1 }
            ]
        });

        var described = PreviewTorrentLookup.Describe(_db, [new MatchedTorrent(2, "bbb"), new MatchedTorrent(1, "aaa")], max: 50);

        Assert.Equal(
        [
            new PreviewTorrent("qbt2", "bbb", "Debian", 7_000, []),
            new PreviewTorrent("qbt1", "aaa", "Ubuntu", 5_000, ["iso", "linux"])
        ], described, new PreviewTorrentComparer());
    }

    [Fact]
    public void Describe_StopsAtMax_AndSkipsPairsMissingFromTheSnapshot()
    {
        _db.Rebuild(new SnapshotInput
        {
            Torrents = [.. Enumerable.Range(0, 5).Select(i =>
                new TorrentRecord { InstanceId = 1, InstanceName = "qbt1", Hash = $"h{i}", Name = $"T{i}", SizeBytes = i, Progress = 1 })]
        });

        var matches = new[] { new MatchedTorrent(1, "gone") }.Concat(Enumerable.Range(0, 5).Select(i => new MatchedTorrent(1, $"h{i}")));
        var described = PreviewTorrentLookup.Describe(_db, matches.ToList(), max: 3);

        Assert.Equal(["h0", "h1", "h2"], described.Select(d => d.Hash));
    }

    private sealed class PreviewTorrentComparer : IEqualityComparer<PreviewTorrent>
    {
        public bool Equals(PreviewTorrent? x, PreviewTorrent? y) =>
            x is not null && y is not null && (x.InstanceName, x.Hash, x.Name, x.SizeBytes) == (y.InstanceName, y.Hash, y.Name, y.SizeBytes)
            && x.Tags.SequenceEqual(y.Tags);
        public int GetHashCode(PreviewTorrent obj) => obj.Hash.GetHashCode();
    }
}

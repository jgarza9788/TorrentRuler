using TorrentRuler.Core.Domain;
using TorrentRuler.Core.Interfaces;

namespace TorrentRuler.Sources.Adapters;

public interface ISourceAdapterResolver
{
    ISourceAdapter Resolve(SourceType type);
}

public class SourceAdapterResolver : ISourceAdapterResolver
{
    private readonly Dictionary<SourceType, ISourceAdapter> _byType;

    public SourceAdapterResolver(IEnumerable<ISourceAdapter> adapters)
    {
        _byType = adapters.ToDictionary(a => a.SourceType);
    }

    public ISourceAdapter Resolve(SourceType type) =>
        _byType.TryGetValue(type, out var adapter)
            ? adapter
            : throw new InvalidOperationException($"No adapter registered for source type {type}.");
}

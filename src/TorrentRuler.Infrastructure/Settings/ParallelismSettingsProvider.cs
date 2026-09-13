using Microsoft.EntityFrameworkCore;
using TorrentRuler.Core.Domain;
using TorrentRuler.Core.Interfaces;
using TorrentRuler.Infrastructure.Persistence;

namespace TorrentRuler.Infrastructure.Settings;

public class ParallelismSettingsProvider(AppDbContext db) : IParallelismSettingsProvider
{
    public async Task<ParallelismLevel> GetLevelAsync(CancellationToken ct = default)
    {
        var settings = await db.AppSettings.AsNoTracking().SingleAsync(s => s.Id == 1, ct);
        return settings.ParallelismLevel;
    }
}

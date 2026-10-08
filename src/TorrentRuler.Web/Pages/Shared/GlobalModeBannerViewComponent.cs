using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TorrentRuler.Infrastructure.Persistence;

namespace TorrentRuler.Web.Pages.Shared;

/// <summary>
/// A banner under the navbar on every page while the global dry run or kill switch is on -- both
/// silently change what every rule does, so they must be impossible to forget about.
/// </summary>
public class GlobalModeBannerViewComponent(AppDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var settings = await db.AppSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == 1);
        return settings is { GlobalKillSwitch: true } or { GlobalDryRun: true }
            ? View((settings.GlobalKillSwitch, settings.GlobalDryRun))
            : Content(string.Empty);
    }
}

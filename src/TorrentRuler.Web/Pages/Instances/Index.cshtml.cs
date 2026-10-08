using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TorrentRuler.Core.Domain;
using TorrentRuler.Core.Domain.SourceData;
using TorrentRuler.Infrastructure.Persistence;
using TorrentRuler.Sources.Storage;

namespace TorrentRuler.Web.Pages.Instances;

public class IndexModel(
    AppDbContext db,
    InstanceConnectionTester connectionTester,
    IStorageUsageService storageUsage) : PageModel
{
    public List<Instance> Instances { get; private set; } = [];
    public List<StoragePathConfig> StoragePaths { get; private set; } = [];

    /// <summary>Current disk usage per storage path id (a cheap filesystem stat, read on every page load).</summary>
    public Dictionary<int, StorageUsageRecord> StorageUsage { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Instances = await db.Instances.AsNoTracking().OrderBy(i => i.Name).ToListAsync(ct);
        StoragePaths = await db.StoragePaths.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        StorageUsage = StoragePaths.ToDictionary(p => p.Id, storageUsage.GetUsage);
    }

    /// <summary>HTMX: test one instance, record the result, and swap in its updated status cell.</summary>
    public async Task<IActionResult> OnPostTestConnectionAsync(int id, CancellationToken ct)
    {
        var result = await connectionTester.TestAndRecordAsync(id, ct);
        if (result is null)
        {
            return NotFound();
        }

        var instance = await db.Instances.AsNoTracking().SingleAsync(i => i.Id == id, ct);
        Toasts.Trigger(Response, result.Success ? ToastKind.Success : ToastKind.Error,
            result.Success ? $"{instance.Name}: connected." : $"{instance.Name}: connection failed.");
        return Partial("_InstanceStatus", instance);
    }

    public async Task<IActionResult> OnPostTestAllAsync(CancellationToken ct)
    {
        var (ok, total) = await connectionTester.TestAllAsync(ct);
        Toasts.Add(TempData, ok == total ? ToastKind.Success : ToastKind.Error,
            total == 0 ? "No enabled instances to test." : $"{ok} of {total} enabled instance(s) connected.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleEnabledAsync(int id, CancellationToken ct)
    {
        var instance = await db.Instances.FindAsync([id], ct);
        if (instance is not null)
        {
            instance.Enabled = !instance.Enabled;
            instance.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteInstanceAsync(int id, CancellationToken ct)
    {
        var instance = await db.Instances.FindAsync([id], ct);
        if (instance is not null)
        {
            db.Instances.Remove(instance);
            await db.SaveChangesAsync(ct);
            Toasts.Add(TempData, ToastKind.Success, $"Deleted instance \"{instance.Name}\".");
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleStoragePathAsync(int id, CancellationToken ct)
    {
        var path = await db.StoragePaths.FindAsync([id], ct);
        if (path is not null)
        {
            path.Enabled = !path.Enabled;
            path.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return RedirectToPage();
    }

    /// <summary>"Scan now": recompute the folder size even if the cached one is still within its interval.</summary>
    public async Task<IActionResult> OnPostScanStorageAsync(int id, CancellationToken ct)
    {
        var path = await db.StoragePaths.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (path is not null)
        {
            var size = await storageUsage.GetOrComputeFolderSizeAsync(path, ct, force: true);
            Toasts.Add(TempData, size is null ? ToastKind.Error : ToastKind.Success,
                size is null ? $"{path.Name}: folder not found." : $"{path.Name}: {Format.Bytes(size.Value)} in folder.");
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteStoragePathAsync(int id, CancellationToken ct)
    {
        var path = await db.StoragePaths.FindAsync([id], ct);
        if (path is not null)
        {
            db.StoragePaths.Remove(path);
            await db.SaveChangesAsync(ct);
            Toasts.Add(TempData, ToastKind.Success, $"Deleted storage path \"{path.Name}\".");
        }
        return RedirectToPage();
    }
}

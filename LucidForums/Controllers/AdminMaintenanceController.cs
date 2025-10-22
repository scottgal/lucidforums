using LucidForums.Helpers;
using LucidForums.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

[Authorize(Roles = "Administrator")]
public class AdminMaintenanceController(IAdminMaintenanceService maintenanceService, TranslationHelper translator) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var (total, indexed) = await maintenanceService.GetIndexingStatusAsync(ct);
        ViewBag.TotalMessages = total;
        ViewBag.IndexedMessages = indexed;
        ViewBag.PercentageIndexed = total > 0 ? (indexed * 100.0 / total) : 0;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IndexAllMessages(CancellationToken ct)
    {
        try
        {
            var indexedCount = await maintenanceService.IndexAllMessagesAsync(ct);
            var template = await translator.T("admin.index-messages.success", "Successfully indexed {0} messages for semantic search.");
            TempData["SuccessMessage"] = string.Format(template, indexedCount);
        }
        catch (Exception ex)
        {
            var template = await translator.T("admin.index-messages.error", "Error during indexing: {0}");
            TempData["ErrorMessage"] = string.Format(template, ex.Message);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearContent(CancellationToken ct)
    {
        try
        {
            await maintenanceService.ClearContentAsync(ct);
            TempData["SuccessMessage"] = await translator.T("admin.clear-content-maintenance.success", "All content has been cleared successfully.");
        }
        catch (Exception ex)
        {
            var template = await translator.T("admin.clear-content-maintenance.error", "Error clearing content: {0}");
            TempData["ErrorMessage"] = string.Format(template, ex.Message);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> GetIndexingStatus(CancellationToken ct)
    {
        var (total, indexed) = await maintenanceService.GetIndexingStatusAsync(ct);
        return Json(new
        {
            total,
            indexed,
            percentage = total > 0 ? (indexed * 100.0 / total) : 0
        });
    }
}

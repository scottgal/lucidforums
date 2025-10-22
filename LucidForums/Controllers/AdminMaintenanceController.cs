using LucidForums.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

[Authorize(Roles = "Administrator")]
public class AdminMaintenanceController(IAdminMaintenanceService maintenanceService) : Controller
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
            TempData["SuccessMessage"] = $"Successfully indexed {indexedCount} messages for semantic search.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error during indexing: {ex.Message}";
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
            TempData["SuccessMessage"] = "All content has been cleared successfully.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error clearing content: {ex.Message}";
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

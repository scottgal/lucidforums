using LucidForums.Helpers;
using LucidForums.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

[Authorize(Roles = "Administrator")]
[Route("Admin")]
public class AdminController : Controller
{
    private readonly IAdminMaintenanceService _maintenance;
    private readonly TranslationHelper _translator;

    public AdminController(IAdminMaintenanceService maintenance, TranslationHelper translator)
    {
        _maintenance = maintenance;
        _translator = translator;
    }

    [HttpGet]
    [Route("Tools")] 
    public IActionResult Tools()
    {
        ViewData["Title"] = "Admin • Tools";
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("ClearContent")]
    public async Task<IActionResult> ClearContent(CancellationToken ct)
    {
        try
        {
            await _maintenance.ClearContentAsync(ct);
            TempData["Message"] = await _translator.T("admin.clear-content.success", "All forum content has been cleared (forums, threads, messages, memberships, embeddings). Settings and user accounts were preserved.");
        }
        catch (Exception ex)
        {
            TempData["Error"] = await _translator.T("admin.clear-content.error", "Failed to clear content: ") + ex.Message;
        }
        // Redirect back to AI Settings page where the Clear button resides
        return RedirectToAction("Index", "AdminAiSettings");
    }
}

using LucidForums.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

[Authorize(Roles = "Administrator")]
[Route("Admin")] 
public class AdminController : Controller
{
    private readonly IAdminMaintenanceService _maintenance;

    public AdminController(IAdminMaintenanceService maintenance)
    {
        _maintenance = maintenance;
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
            TempData["Message"] = "All forum content has been cleared (forums, threads, messages, memberships, embeddings). Settings and user accounts were preserved.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Failed to clear content: " + ex.Message;
        }
        // Redirect back to AI Settings page where the Clear button resides
        return RedirectToAction("Index", "AdminAiSettings");
    }
}

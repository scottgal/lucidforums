using LucidForums.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

[Authorize]
[Route("Admin")] 
public class AdminController(IAdminMaintenanceService maintenance) : Controller
{
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
            await maintenance.ClearContentAsync(ct);
            TempData["Message"] = "All forum content has been cleared (forums, threads, messages, memberships, embeddings). Settings and user accounts were preserved.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Failed to clear content: " + ex.Message;
        }
        // Redirect back to AI Settings page where the Clear button resides to avoid 404 if Tools view is absent
        return Redirect("/Admin/AiSettings");
    }
}

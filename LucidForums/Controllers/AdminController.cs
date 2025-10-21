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
        await maintenance.ClearContentAsync(ct);
        TempData["Message"] = "All forum content has been cleared (forums, threads, messages, memberships, embeddings). Settings and user accounts were preserved.";
        return RedirectToAction("Tools");
    }
}

using LucidForums.Services.Llm;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

public class SettingsController(OllamaOptions ollamaOptions) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        // Unify with AI Settings page
        return RedirectToAction("Index", "AdminAiSettings", new { area = "" });
    }
}
using LucidForums.Services.Llm;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

public class SettingsController(OllamaOptions ollamaOptions) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Title"] = "Settings";
        return View(ollamaOptions);
    }
}
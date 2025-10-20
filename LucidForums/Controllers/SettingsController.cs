using LucidForums.Services.Llm;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LucidForums.Controllers;

public class SettingsController(IOptions<OllamaOptions> ollamaOptions) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Title"] = "Settings";
        return View(ollamaOptions.Value);
    }
}
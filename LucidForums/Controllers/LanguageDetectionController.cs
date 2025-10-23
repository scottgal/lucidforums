using LucidForums.Services.Translation;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LanguageDetectionController : ControllerBase
{
    private readonly ITranslationService _translationService;
    private readonly ILogger<LanguageDetectionController> _logger;

    public LanguageDetectionController(
        ITranslationService translationService,
        ILogger<LanguageDetectionController> logger)
    {
        _translationService = translationService;
        _logger = logger;
    }

    [HttpPost("detect")]
    public async Task<IActionResult> DetectLanguage([FromBody] DetectLanguageRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { error = "Text is required" });
        }

        try
        {
            // Use the translation service to detect language
            // We'll create a helper method in TranslationService for this
            var languageCode = await _translationService.DetectLanguageAsync(request.Text, ct);

            return Ok(new { languageCode });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect language for text");
            return StatusCode(500, new { error = "Language detection failed" });
        }
    }
}

public record DetectLanguageRequest(string Text);

using System.Threading;
using System.Threading.Tasks;

namespace LucidForums.Services.Ai;

public interface IImageAiService
{
    /// <summary>
    /// Generate an image based on a textual prompt and return a data URL string or external URL.
    /// </summary>
    Task<string> GenerateImageAsync(string prompt, string? model = null, CancellationToken ct = default);
}
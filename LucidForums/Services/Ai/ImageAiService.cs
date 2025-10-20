using System;
using System.Threading;
using System.Threading.Tasks;

namespace LucidForums.Services.Ai;

public class ImageAiService : IImageAiService
{
    public Task<string> GenerateImageAsync(string prompt, string? model = null, CancellationToken ct = default)
    {
        throw new NotSupportedException("No image generator is configured yet. Plug in a provider (e.g., OpenAI) and implement via Microsoft.Extensions.AI.");
    }
}
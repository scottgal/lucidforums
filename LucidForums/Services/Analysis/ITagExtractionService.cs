using System.Text.RegularExpressions;

namespace LucidForums.Services.Analysis;

public interface ITagExtractionService
{
    Task<IReadOnlyList<string>> ExtractAsync(string text, int maxTags = 5, CancellationToken ct = default);
}

public class TagExtractionService : ITagExtractionService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","a","an","and","or","but","if","then","else","when","at","by","for","in","of","on","to","with","as","is","are","was","were","be","been","it","this","that","these","those","i","you","he","she","we","they","them","me","my","your","our","their","from","so","not","no","yes","do","does","did"
    };

    public Task<IReadOnlyList<string>> ExtractAsync(string text, int maxTags = 5, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.FromResult((IReadOnlyList<string>)Array.Empty<string>());
        // Simple heuristic: find words 3+ chars, remove punctuation, count frequency, exclude stopwords
        var normalized = text.ToLowerInvariant();
        var matches = Regex.Matches(normalized, "[a-z0-9][a-z0-9\\-]{2,}");
        var freq = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in matches)
        {
            var w = m.Value.Trim('-');
            if (w.Length < 3) continue;
            if (StopWords.Contains(w)) continue;
            freq[w] = freq.TryGetValue(w, out var c) ? c + 1 : 1;
        }
        var tags = freq.OrderByDescending(kv => kv.Value)
                       .ThenBy(kv => kv.Key)
                       .Take(Math.Max(1, Math.Min(10, maxTags)))
                       .Select(kv => kv.Key)
                       .ToList();
        return Task.FromResult((IReadOnlyList<string>)tags);
    }
}

using System.Text.RegularExpressions;
using LucidForums.Models.Entities;
using LucidForums.Services.Ai;

namespace LucidForums.Services.Analysis;

public class CharterScoringService(ITextAiService ai) : ICharterScoringService
{
    public async Task<double?> ScoreAsync(Charter? charter, string? text, CancellationToken ct = default)
    {
        if (charter is null) return null;
        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            var prompt = "You are scoring a piece of forum content for compliance with the community charter. " +
                         "Return ONLY a number from 0 to 100 where 0 = violates charter heavily, 100 = perfectly aligned. " +
                         "Do not include any words or symbols, only the number.\n\n" +
                         "Content:" + "\n" + text.Trim();

            var result = await ai.GenerateAsync(charter, prompt, ct: ct);
            if (string.IsNullOrWhiteSpace(result)) return null;

            // Extract first number 0-100 (integer or decimal)
            var m = Regex.Match(result, "(?<![0-9])(100|[0-9]{1,2})(?:\\.[0-9]+)?");
            if (!m.Success) return null;
            if (!double.TryParse(m.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var score))
                return null;
            // Clamp
            score = Math.Clamp(score, 0, 100);
            return score;
        }
        catch
        {
            return null; // best-effort
        }
    }
}
using LucidForums.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Services.Analysis;

public interface IToneAdvisor
{
    Task<string?> CreateAdviceAsync(string content, CancellationToken ct = default);
}

public class ToneAdvisor(ApplicationDbContext db) : IToneAdvisor
{
    public async Task<string?> CreateAdviceAsync(string content, CancellationToken ct = default)
    {
        // Very lightweight heuristic that references existing charters’ behaviors/rules.
        try
        {
            var behaviors = await db.Charters.AsNoTracking()
                .SelectMany(c => c.Behaviors)
                .Where(b => b != null)
                .Distinct()
                .Take(5)
                .ToListAsync(ct);
            var rules = await db.Charters.AsNoTracking()
                .SelectMany(c => c.Rules)
                .Where(r => r != null)
                .Distinct()
                .Take(5)
                .ToListAsync(ct);

            if (behaviors.Count == 0 && rules.Count == 0) return null;

            // Create a short advisory note. We don’t run more AI here to keep it cheap and deterministic.
            var bullets = new List<string>();
            if (behaviors.Count > 0) bullets.Add($"Behaviors: {string.Join(", ", behaviors.Take(3))}.");
            if (rules.Count > 0) bullets.Add($"Rules: {string.Join(", ", rules.Take(3))}.");
            var advice = "Consider aligning with community charter — " + string.Join(" ", bullets);
            return advice;
        }
        catch
        {
            return null;
        }
    }
}

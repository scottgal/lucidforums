using LucidForums.Models.Entities;

namespace LucidForums.Services.Analysis;

public interface ICharterScoringService
{
    /// <summary>
    /// Computes a 0-100 score for how well the given text aligns with the provided charter.
    /// Returns null if the charter is null or if scoring fails.
    /// </summary>
    Task<double?> ScoreAsync(Charter? charter, string? text, CancellationToken ct = default);
}
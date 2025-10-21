namespace LucidForums.Models.Entities;

public class AppSettings
{
    public int Id { get; set; } = 1; // single-row table

    // AI settings persisted
    public string? GenerationProvider { get; set; }
    public string? GenerationModel { get; set; }
    public string? TranslationProvider { get; set; }
    public string? TranslationModel { get; set; }
    public string? EmbeddingProvider { get; set; }
    public string? EmbeddingModel { get; set; }
}
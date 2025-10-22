namespace mostlylucid.llmtranslate.Models;

public record TranslationStringDto(string Key, string DefaultText, string? TranslatedText);

public record TranslationProgress(int Total, int Completed, string? CurrentKey);

public record TranslationStats(
    string LanguageCode,
    int TotalStrings,
    int TranslatedStrings,
    int PendingStrings,
    double CompletionPercentage
);

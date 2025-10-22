namespace mostlylucid.llmtranslate.Services;

/// <summary>
/// Builds the HTMX OOB response for switching UI language and triggers background translations for missing strings.
/// </summary>
public interface IPageLanguageSwitchService
{
    /// <summary>
    /// Builds OOB swap HTML for provided keys and language. Also starts background batch translation for any missing
    /// strings and broadcasts per-string updates via SignalR as they are stored.
    /// </summary>
    /// <param name="languageCode">Target ISO language code.</param>
    /// <param name="keys">Translation keys present on the page.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Concatenated HTML string containing OOB swap elements.</returns>
    Task<string> BuildSwitchResponseAsync(string languageCode, IEnumerable<string> keys, CancellationToken ct = default);
}

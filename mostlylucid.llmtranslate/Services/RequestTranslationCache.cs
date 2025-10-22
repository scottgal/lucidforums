namespace mostlylucid.llmtranslate.Services;

/// <summary>
/// Request-scoped cache for translations to avoid concurrent DbContext access
/// </summary>
public class RequestTranslationCache
{
    private readonly Dictionary<string, Dictionary<string, string>> _cache = new();
    private readonly object _lock = new();

    public bool TryGet(string languageCode, string key, out string? value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(languageCode, out var langCache))
            {
                return langCache.TryGetValue(key, out value);
            }
            value = null;
            return false;
        }
    }

    public void Set(string languageCode, string key, string value)
    {
        lock (_lock)
        {
            if (!_cache.ContainsKey(languageCode))
            {
                _cache[languageCode] = new Dictionary<string, string>();
            }
            _cache[languageCode][key] = value;
        }
    }

    public void SetBatch(string languageCode, Dictionary<string, string> translations)
    {
        lock (_lock)
        {
            _cache[languageCode] = new Dictionary<string, string>(translations);
        }
    }
}

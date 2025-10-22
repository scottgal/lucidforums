using System.Text.Json;
using Microsoft.Extensions.Logging;
using mostlylucid.llmtranslate.Models;

namespace mostlylucid.llmtranslate.Storage;

/// <summary>
/// JSON file-based storage for translations
/// Useful for simple deployments, development, or static site generation
/// </summary>
public class JsonFileTranslationStore
{
    private readonly string _filePath;
    private readonly bool _autoSave;
    private readonly ILogger<JsonFileTranslationStore> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private JsonTranslationData _data;

    public JsonFileTranslationStore(string filePath, bool autoSave, ILogger<JsonFileTranslationStore> logger)
    {
        _filePath = filePath;
        _autoSave = autoSave;
        _logger = logger;

        // Ensure directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Load existing data or create new
        _data = LoadFromFile();
    }

    private JsonTranslationData LoadFromFile()
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("Translation file not found at {Path}, creating new", _filePath);
            return new JsonTranslationData();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<JsonTranslationData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _logger.LogInformation("Loaded {StringCount} translation strings from {Path}",
                data?.Strings.Count ?? 0, _filePath);

            return data ?? new JsonTranslationData();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load translations from {Path}, starting with empty data", _filePath);
            return new JsonTranslationData();
        }
    }

    public async Task SaveToFileAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await File.WriteAllTextAsync(_filePath, json, ct);
            _logger.LogDebug("Saved {StringCount} translation strings to {Path}", _data.Strings.Count, _filePath);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<TranslationString?> GetStringAsync(string key, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            return _data.Strings.FirstOrDefault(s => s.Key == key);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<List<TranslationString>> GetAllStringsAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            return _data.Strings.ToList();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<int> UpsertStringAsync(TranslationString translationString, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var existing = _data.Strings.FirstOrDefault(s => s.Key == translationString.Key);
            if (existing != null)
            {
                existing.DefaultText = translationString.DefaultText;
                existing.Context = translationString.Context;
                existing.Category = translationString.Category;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                if (translationString.Id == 0)
                {
                    translationString.Id = _data.Strings.Count > 0 ? _data.Strings.Max(s => s.Id) + 1 : 1;
                }
                _data.Strings.Add(translationString);
            }

            if (_autoSave)
            {
                await SaveToFileAsync(ct);
            }

            return translationString.Id;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<Translation?> GetTranslationAsync(string key, string languageCode, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var translationString = _data.Strings.FirstOrDefault(s => s.Key == key);
            if (translationString == null) return null;

            return translationString.Translations.FirstOrDefault(t => t.LanguageCode == languageCode);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<List<Translation>> GetAllTranslationsForLanguageAsync(string languageCode, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            return _data.Strings
                .SelectMany(s => s.Translations.Where(t => t.LanguageCode == languageCode))
                .ToList();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task UpsertTranslationAsync(int translationStringId, Translation translation, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var translationString = _data.Strings.FirstOrDefault(s => s.Id == translationStringId);
            if (translationString == null)
            {
                _logger.LogWarning("Translation string with ID {Id} not found", translationStringId);
                return;
            }

            var existing = translationString.Translations.FirstOrDefault(t =>
                t.LanguageCode == translation.LanguageCode);

            if (existing != null)
            {
                existing.TranslatedText = translation.TranslatedText;
                existing.Source = translation.Source;
                existing.AiModel = translation.AiModel;
                existing.IsApproved = translation.IsApproved;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                if (translation.Id == 0)
                {
                    var maxId = _data.Strings.SelectMany(s => s.Translations).DefaultIfEmpty().Max(t => t?.Id ?? 0);
                    translation.Id = maxId + 1;
                }
                translation.TranslationStringId = translationStringId;
                translationString.Translations.Add(translation);
            }

            if (_autoSave)
            {
                await SaveToFileAsync(ct);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<List<string>> GetAvailableLanguagesAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var languages = _data.Strings
                .SelectMany(s => s.Translations.Select(t => t.LanguageCode))
                .Distinct()
                .OrderBy(l => l)
                .ToList();

            if (!languages.Contains("en"))
            {
                languages.Insert(0, "en");
            }

            return languages;
        }
        finally
        {
            _fileLock.Release();
        }
    }
}

/// <summary>
/// Root data structure for JSON file storage
/// </summary>
public class JsonTranslationData
{
    public List<TranslationString> Strings { get; set; } = new();
}

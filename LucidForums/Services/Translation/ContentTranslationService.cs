using System.Security.Cryptography;
using System.Text;
using LucidForums.Data;
using LucidForums.Hubs;
using LucidForums.Models.Entities;
using LucidForums.Services.Ai;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Services.Translation;

public class ContentTranslationService : IContentTranslationService
{
    private readonly ApplicationDbContext _db;
    private readonly ITextAiService _ai;
    private readonly ILogger<ContentTranslationService> _logger;
    private readonly IHubContext<TranslationHub> _hubContext;

    public ContentTranslationService(
        ApplicationDbContext db,
        ITextAiService ai,
        ILogger<ContentTranslationService> logger,
        IHubContext<TranslationHub> hubContext)
    {
        _db = db;
        _ai = ai;
        _logger = logger;
        _hubContext = hubContext;
    }

    public async Task<string?> GetTranslationAsync(string contentType, string contentId, string fieldName, string languageCode, CancellationToken ct = default)
    {
        var translation = await _db.ContentTranslations
            .FirstOrDefaultAsync(t =>
                t.ContentType == contentType &&
                t.ContentId == contentId &&
                t.FieldName == fieldName &&
                t.LanguageCode == languageCode &&
                !t.IsStale, ct);

        return translation?.TranslatedText;
    }

    public async Task<string> TranslateContentAsync(string contentType, string contentId, string fieldName, string sourceText, string targetLanguage, CancellationToken ct = default)
    {
        var sourceHash = ComputeHash(sourceText);

        // Check if we have a valid cached translation
        var existing = await _db.ContentTranslations
            .FirstOrDefaultAsync(t =>
                t.ContentType == contentType &&
                t.ContentId == contentId &&
                t.FieldName == fieldName &&
                t.LanguageCode == targetLanguage, ct);

        if (existing != null && existing.SourceHash == sourceHash && !existing.IsStale)
        {
            return existing.TranslatedText;
        }

        // Translate using AI
        var charter = new Charter
        {
            Name = "Content Translation",
            Purpose = $"Translate {contentType} content accurately while preserving meaning and tone"
        };

        var prompt = $@"Translate the following {contentType} {fieldName} to {targetLanguage}.
Preserve the original tone and meaning. Maintain paragraph breaks and formatting.
Return ONLY the translated text, no explanations.

Text to translate:
{sourceText}";

        var translatedText = await _ai.GenerateAsync(charter, prompt, ct: ct);
        translatedText = translatedText?.Trim() ?? sourceText;

        // Save or update translation
        if (existing != null)
        {
            existing.TranslatedText = translatedText;
            existing.SourceHash = sourceHash;
            existing.IsStale = false;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            var newTranslation = new ContentTranslation
            {
                ContentType = contentType,
                ContentId = contentId,
                FieldName = fieldName,
                LanguageCode = targetLanguage,
                TranslatedText = translatedText,
                SourceHash = sourceHash,
                Source = TranslationSource.AiGenerated,
                AiModel = "Content Translation AI"
            };
            _db.ContentTranslations.Add(newTranslation);
        }

        await _db.SaveChangesAsync(ct);

        // Broadcast the translation via SignalR so connected clients can update in real-time
        _ = Task.Run(async () =>
        {
            try
            {
                await _hubContext.Clients.All.SendAsync(
                    "ContentTranslated",
                    new
                    {
                        contentType,
                        contentId,
                        fieldName,
                        language = targetLanguage,
                        translatedText
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast translation via SignalR");
            }
        });

        return translatedText;
    }

    public async Task<int> TranslateAllContentAsync(string contentType, string targetLanguage, IProgress<TranslationProgress>? progress = null, CancellationToken ct = default)
    {
        var translated = 0;

        switch (contentType.ToLower())
        {
            case "forum":
                translated = await TranslateForumsAsync(targetLanguage, progress, ct);
                break;
            case "thread":
                translated = await TranslateThreadsAsync(targetLanguage, progress, ct);
                break;
            case "message":
                translated = await TranslateMessagesAsync(targetLanguage, progress, ct);
                break;
            default:
                throw new ArgumentException($"Unknown content type: {contentType}");
        }

        return translated;
    }

    public async Task MarkStaleAsync(string contentType, string contentId, string fieldName, CancellationToken ct = default)
    {
        var translations = await _db.ContentTranslations
            .Where(t =>
                t.ContentType == contentType &&
                t.ContentId == contentId &&
                t.FieldName == fieldName)
            .ToListAsync(ct);

        foreach (var translation in translations)
        {
            translation.IsStale = true;
            translation.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<int> TranslateForumsAsync(string targetLanguage, IProgress<TranslationProgress>? progress, CancellationToken ct)
    {
        var forums = await _db.Forums.ToListAsync(ct);
        var total = forums.Count * 2; // Name + Description
        var completed = 0;
        var translated = 0;

        foreach (var forum in forums)
        {
            ct.ThrowIfCancellationRequested();

            progress?.Report(new TranslationProgress(total, completed, $"Forum: {forum.Name}"));

            // Translate name
            await TranslateContentAsync("Forum", forum.Id.ToString(), "Name", forum.Name, targetLanguage, ct);
            completed++;
            translated++;

            // Translate description if exists
            if (!string.IsNullOrWhiteSpace(forum.Description))
            {
                await TranslateContentAsync("Forum", forum.Id.ToString(), "Description", forum.Description, targetLanguage, ct);
                translated++;
            }
            completed++;
        }

        return translated;
    }

    private async Task<int> TranslateThreadsAsync(string targetLanguage, IProgress<TranslationProgress>? progress, CancellationToken ct)
    {
        var threads = await _db.Threads.ToListAsync(ct);
        var total = threads.Count;
        var completed = 0;
        var translated = 0;

        foreach (var thread in threads)
        {
            ct.ThrowIfCancellationRequested();

            progress?.Report(new TranslationProgress(total, completed, $"Thread: {thread.Title}"));

            await TranslateContentAsync("Thread", thread.Id.ToString(), "Title", thread.Title, targetLanguage, ct);
            completed++;
            translated++;
        }

        return translated;
    }

    private async Task<int> TranslateMessagesAsync(string targetLanguage, IProgress<TranslationProgress>? progress, CancellationToken ct)
    {
        var messages = await _db.Messages.ToListAsync(ct);
        var total = messages.Count;
        var completed = 0;
        var translated = 0;

        foreach (var message in messages)
        {
            ct.ThrowIfCancellationRequested();

            progress?.Report(new TranslationProgress(total, completed, $"Message {completed + 1}/{total}"));

            await TranslateContentAsync("Message", message.Id.ToString(), "Content", message.Content, targetLanguage, ct);
            completed++;
            translated++;
        }

        return translated;
    }

    private static string ComputeHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}

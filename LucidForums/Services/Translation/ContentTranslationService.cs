using System.Security.Cryptography;
using System.Text;
using LucidForums.Data;
using LucidForums.Hubs;
using LucidForums.Models.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Services.Translation;

public class ContentTranslationService : IContentTranslationService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ITranslationService _translationService;
    private readonly ILogger<ContentTranslationService> _logger;
    private readonly IHubContext<TranslationHub> _hubContext;
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    public ContentTranslationService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        ITranslationService translationService,
        ILogger<ContentTranslationService> logger,
        IHubContext<TranslationHub> hubContext)
    {
        _dbFactory = dbFactory;
        _translationService = translationService;
        _logger = logger;
        _hubContext = hubContext;
    }

    public async Task<string?> GetTranslationAsync(string contentType, string contentId, string fieldName, string languageCode, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var translation = await db.ContentTranslations
                .AsNoTracking()
                .FirstOrDefaultAsync(t =>
                    t.ContentType == contentType &&
                    t.ContentId == contentId &&
                    t.FieldName == fieldName &&
                    t.LanguageCode == languageCode &&
                    !t.IsStale, ct);

            return translation?.TranslatedText;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<string> TranslateContentAsync(string contentType, string contentId, string fieldName, string sourceText, string targetLanguage, CancellationToken ct = default)
    {
        var sourceHash = ComputeHash(sourceText);
        ContentTranslation? existing = null;

        await _dbLock.WaitAsync(ct);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // OPTIMIZATION: First check if ANY content with this hash + language already has a translation
            // This allows reusing translations across different threads/messages with identical content
            var reusableTranslation = await db.ContentTranslations
                .AsNoTracking()
                .FirstOrDefaultAsync(t =>
                    t.SourceHash == sourceHash &&
                    t.LanguageCode == targetLanguage &&
                    !t.IsStale, ct);

            if (reusableTranslation != null)
            {
                // Found a reusable translation! Store a reference for this specific content
                var reference = await db.ContentTranslations
                    .FirstOrDefaultAsync(t =>
                        t.ContentType == contentType &&
                        t.ContentId == contentId &&
                        t.FieldName == fieldName &&
                        t.LanguageCode == targetLanguage, ct);

                if (reference == null)
                {
                    // Create a reference entry pointing to the same translation
                    reference = new ContentTranslation
                    {
                        ContentType = contentType,
                        ContentId = contentId,
                        FieldName = fieldName,
                        LanguageCode = targetLanguage,
                        TranslatedText = reusableTranslation.TranslatedText,
                        SourceHash = sourceHash,
                        Source = reusableTranslation.Source,
                        AiModel = reusableTranslation.AiModel
                    };
                    db.ContentTranslations.Add(reference);
                    await db.SaveChangesAsync(ct);
                }

                return reusableTranslation.TranslatedText;
            }

            // Check if we have a cached translation for this specific content
            existing = await db.ContentTranslations
                .FirstOrDefaultAsync(t =>
                    t.ContentType == contentType &&
                    t.ContentId == contentId &&
                    t.FieldName == fieldName &&
                    t.LanguageCode == targetLanguage, ct);

            if (existing != null && existing.SourceHash == sourceHash && !existing.IsStale)
            {
                return existing.TranslatedText;
            }
        }
        finally
        {
            _dbLock.Release();
        }

        // Notify clients that this content has been queued for translation (for subtle UI framing)
        _ = Task.Run(async () =>
        {
            try
            {
                await _hubContext.Clients.All.SendAsync(
                    "ContentTranslationQueued",
                    new
                    {
                        contentType,
                        contentId,
                        fieldName,
                        language = targetLanguage
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast translation queued via SignalR");
            }
        });

        // Translate using TranslationService (which uses EasyNMT with LLM fallback)
        var translatedText = await _translationService.TranslateAsync(sourceText, targetLanguage, sourceLanguage: "auto", ct: ct);

        // Save or update translation (with lock to prevent concurrent DbContext access)
        await _dbLock.WaitAsync(ct);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

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
                    AiModel = "EasyNMT/LLM"
                };
                db.ContentTranslations.Add(newTranslation);
            }

            await db.SaveChangesAsync(ct);
        }
        finally
        {
            _dbLock.Release();
        }

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
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var translations = await db.ContentTranslations
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

        await db.SaveChangesAsync(ct);
    }

    private async Task<int> TranslateForumsAsync(string targetLanguage, IProgress<TranslationProgress>? progress, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var forums = await db.Forums.AsNoTracking().ToListAsync(ct);
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
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var threads = await db.Threads.AsNoTracking().ToListAsync(ct);
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
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var messages = await db.Messages.AsNoTracking().ToListAsync(ct);
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

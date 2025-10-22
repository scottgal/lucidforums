using LucidForums.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace LucidForums.Services.Translation;

/// <summary>
/// Background service that processes content translation queue
/// </summary>
public class ContentTranslationHostedService : BackgroundService
{
    private readonly ContentTranslationQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ContentTranslationHostedService> _logger;
    private readonly IHubContext<TranslationHub> _hubContext;

    public ContentTranslationHostedService(
        ContentTranslationQueue queue,
        IServiceProvider serviceProvider,
        ILogger<ContentTranslationHostedService> logger,
        IHubContext<TranslationHub> hubContext)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Content translation background service started");

        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessTranslationAsync(item, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing translation for {ContentType} {ContentId}",
                    item.ContentType, item.ContentId);
            }
        }
    }

    private async Task ProcessTranslationAsync(TranslationQueueItem item, CancellationToken ct)
    {
        // Instead of translating to all languages upfront, we just log that the content is available for translation
        // Actual translation happens on-demand when a user views the content in their language
        // This is more efficient and only translates what's actually needed

        _logger.LogInformation("Content queued for translation: {ContentType} {ContentId} field {FieldName}",
            item.ContentType, item.ContentId, item.FieldName);

        // Optional: Pre-translate to most common languages if desired
        // For now, we'll skip this and rely on on-demand translation
    }
}

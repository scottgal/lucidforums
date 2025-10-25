using LucidForums.Hubs;
using LucidForums.Models.Entities;
using Microsoft.AspNetCore.SignalR;

namespace LucidForums.Services.Translation;

/// <summary>
/// Background service that processes content translation queue
/// </summary>
public class ContentTranslationHostedService : BackgroundService
{
    private readonly IContentTranslationQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ContentTranslationHostedService> _logger;
    private readonly IHubContext<TranslationHub> _hubContext;
    private readonly IConfiguration _configuration;

    public ContentTranslationHostedService(
        IContentTranslationQueue queue,
        IServiceProvider serviceProvider,
        ILogger<ContentTranslationHostedService> logger,
        IHubContext<TranslationHub> hubContext,
        IConfiguration configuration)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _hubContext = hubContext;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Content translation background service started (PostgreSQL-backed queue)");

        // Ping EasyNMT on startup to verify connectivity
        await PingEasyNmtAsync(stoppingToken);

        // Log any existing items in the queue from previous runs
        var pendingCount = await _queue.GetPendingCountAsync(stoppingToken);
        if (pendingCount > 0)
        {
            _logger.LogInformation("Found {Count} pending translation items from previous runs", pendingCount);
        }

        // Process queue with polling (persistent across restarts)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var item = await _queue.DequeueAsync(stoppingToken);
                if (item != null)
                {
                    try
                    {
                        await ProcessTranslationAsync(item, stoppingToken);
                        await _queue.MarkProcessedAsync(item.Id, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing translation for {ContentType} {ContentId}",
                            item.ContentType, item.ContentId);
                        await _queue.MarkFailedAsync(item.Id, ex.Message, stoppingToken);
                    }
                }
                else
                {
                    // No items in queue, wait before polling again
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }

                // Periodically clean up old processed items (every 100 items processed)
                if (Random.Shared.Next(100) == 0)
                {
                    await _queue.CleanupOldItemsAsync(daysToKeep: 7, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in translation service");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("Content translation background service stopped");
    }

    private async Task PingEasyNmtAsync(CancellationToken ct)
    {
        try
        {
            var translationProvider = _configuration["Translation:Provider"];
            var easynmtEndpoint = _configuration["EASYNMT_ENDPOINT"] ?? _configuration["Translation:EasyNmt:Endpoint"];

            _logger.LogInformation("Translation Configuration:");
            _logger.LogInformation("  Provider: {Provider}", translationProvider ?? "(not set)");
            _logger.LogInformation("  EasyNMT Endpoint: {Endpoint}", easynmtEndpoint ?? "(not set)");

            if (string.IsNullOrWhiteSpace(translationProvider) || !translationProvider.Equals("easynmt", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("EasyNMT is not configured as the translation provider. Translation will fall back to LLM.");
                return;
            }

            if (string.IsNullOrWhiteSpace(easynmtEndpoint))
            {
                _logger.LogWarning("EasyNMT endpoint is not configured. Translation will fall back to LLM.");
                return;
            }

            // Try to ping EasyNMT
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var baseUrl = easynmtEndpoint.TrimEnd('/');

            _logger.LogInformation("Pinging EasyNMT at {Url}...", baseUrl);

            // Try the /docs endpoint first (FastAPI auto-generates this)
            try
            {
                var response = await httpClient.GetAsync($"{baseUrl}/docs", ct);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("✓ EasyNMT is ONLINE at {Url}", baseUrl);
                    _logger.LogInformation("✓ Translation endpoint available at {Url}/translate", baseUrl);

                    // Get supported languages
                    try
                    {
                        var langResponse = await httpClient.GetAsync($"{baseUrl}/get_languages", ct);
                        if (langResponse.IsSuccessStatusCode)
                        {
                            var languages = await langResponse.Content.ReadFromJsonAsync<string[]>(ct);
                            if (languages != null && languages.Length > 0)
                            {
                                _logger.LogInformation("✓ EasyNMT supports {Count} languages", languages.Length);
                                _logger.LogInformation("  Sample languages: {Languages}", string.Join(", ", languages.Take(10)));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not fetch supported languages from EasyNMT");
                    }

                    // Get supported language pairs
                    try
                    {
                        var pairsResponse = await httpClient.GetAsync($"{baseUrl}/lang_pairs", ct);
                        if (pairsResponse.IsSuccessStatusCode)
                        {
                            var pairs = await pairsResponse.Content.ReadFromJsonAsync<string[]>(ct);
                            if (pairs != null && pairs.Length > 0)
                            {
                                _logger.LogInformation("✓ EasyNMT supports {Count} language pairs", pairs.Length);
                                // Check for common pairs
                                var commonPairs = new[] { "en-es", "en-fr", "en-de", "en-zh", "en-ja" };
                                var available = commonPairs.Where(p => pairs.Contains(p)).ToList();
                                if (available.Any())
                                {
                                    _logger.LogInformation("  Common pairs available: {Pairs}", string.Join(", ", available));
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not fetch language pairs from EasyNMT");
                    }
                }
                else
                {
                    _logger.LogWarning("EasyNMT responded with status {Status} at {Url}", response.StatusCode, baseUrl);
                }
            }
            catch (HttpRequestException hex)
            {
                _logger.LogError("✗ EasyNMT is OFFLINE at {Url}: {Error}", baseUrl, hex.Message);
                _logger.LogWarning("Translations will fall back to LLM (Ollama)");
            }
            catch (TaskCanceledException)
            {
                _logger.LogError("✗ EasyNMT connection TIMEOUT at {Url}", baseUrl);
                _logger.LogWarning("Translations will fall back to LLM (Ollama)");
            }

            // Also check if IAiTranslationProvider is registered
            using var scope = _serviceProvider.CreateScope();
            var providerType = Type.GetType("mostlylucid.llmtranslate.Services.IAiTranslationProvider, mostlylucid.llmtranslate");
            if (providerType != null)
            {
                var provider = scope.ServiceProvider.GetService(providerType);
                if (provider != null)
                {
                    _logger.LogInformation("✓ IAiTranslationProvider is registered in DI container: {Type}", provider.GetType().Name);
                }
                else
                {
                    _logger.LogWarning("✗ IAiTranslationProvider is NOT registered in DI container");
                }
            }
            else
            {
                _logger.LogWarning("✗ Could not find IAiTranslationProvider type");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pinging EasyNMT during startup");
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

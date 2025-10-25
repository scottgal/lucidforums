using LucidForums.Data;
using LucidForums.Models.Configuration;
using LucidForums.Services.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LucidForums.Services.Seeding;

/// <summary>
/// Background service that automatically seeds the database with multi-language content on startup
/// </summary>
public class AutoSeedingHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutoSeedingHostedService> _logger;
    private readonly SeedingOptions _options;

    public AutoSeedingHostedService(
        IServiceProvider serviceProvider,
        ILogger<AutoSeedingHostedService> logger,
        IOptions<SeedingOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.AutoSeedOnStartup)
        {
            _logger.LogInformation("Auto-seeding is disabled in configuration");
            return;
        }

        _logger.LogInformation("Auto-seeding enabled: Forums={Forums}, ThreadsPerForum={Threads}, RepliesPerThread={Replies}",
            _options.Forums, _options.ThreadsPerForum, _options.RepliesPerThread);

        // Wait a bit for the app to fully start
        await Task.Delay(5000, stoppingToken);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

            // Check if we already have content
            await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
            var forumCount = await db.Forums.CountAsync(stoppingToken);

            if (forumCount > 0)
            {
                _logger.LogInformation("Database already has {Count} forums, skipping auto-seeding", forumCount);
                return;
            }

            _logger.LogInformation("Starting automatic multi-language content generation...");

            // Use the site setup service to generate content
            var siteSetup = scope.ServiceProvider.GetRequiredService<ISiteSetupService>();

            await siteSetup.GenerateMultiLanguageContentAsync(
                forumCount: _options.Forums,
                threadsPerForum: _options.ThreadsPerForum,
                repliesPerThread: _options.RepliesPerThread,
                languages: _options.Languages,
                forumDelayMs: _options.DelayBetweenForumsMs,
                threadDelayMs: _options.DelayBetweenThreadsMs,
                replyDelayMs: _options.DelayBetweenRepliesMs,
                ct: stoppingToken);

            _logger.LogInformation("Auto-seeding completed successfully!");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Auto-seeding was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during auto-seeding");
        }
    }
}

namespace LucidForums.Models.Configuration;

public class SeedingOptions
{
    public const string ConfigSection = "Seeding";

    public bool AutoSeedOnStartup { get; set; } = false;
    public int Forums { get; set; } = 5;
    public int ThreadsPerForum { get; set; } = 10;
    public int RepliesPerThread { get; set; } = 8;
    public string[] Languages { get; set; } = new[] { "en", "es", "fr", "de", "ja", "zh", "pt", "it", "ru", "ar" };
    public int DelayBetweenForumsMs { get; set; } = 2000;
    public int DelayBetweenThreadsMs { get; set; } = 1000;
    public int DelayBetweenRepliesMs { get; set; } = 500;
}

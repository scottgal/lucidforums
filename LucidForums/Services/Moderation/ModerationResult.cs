namespace LucidForums.Services.Moderation;

public enum ModerationDecision
{
    Allow,
    Flag,
    Reject
}

public class ModerationResult
{
    public ModerationDecision Decision { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Violations { get; set; } = new();
}
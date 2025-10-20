namespace LucidForums.Models.Entities;

public class Charter
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // A friendly name for this charter (e.g., "Tech Community", "Mods Charter")
    public string Name { get; set; } = string.Empty;

    // Overall purpose/mission of the community
    public string Purpose { get; set; } = string.Empty;

    // Rules users must follow (e.g., "No hate speech", "Be respectful")
    public List<string> Rules { get; set; } = new();

    // Desired behaviors (e.g., "Encourage constructive feedback")
    public List<string> Behaviors { get; set; } = new();

    // A helper to compose a consistent system prompt for LLM calls
    public string BuildSystemPrompt()
    {
        var rules = Rules is { Count: > 0 } ? "- " + string.Join("\n- ", Rules) : "(none specified)";
        var behaviors = Behaviors is { Count: > 0 } ? "- " + string.Join("\n- ", Behaviors) : "(none specified)";

        return $@"You are an assistant operating within the following community charter.
Community Name: {Name}
Purpose: {Purpose}

Rules:
{rules}

Expected Behaviors:
{behaviors}

When evaluating or generating content, always apply the charter above. Be concise and actionable in responses.";
    }
}
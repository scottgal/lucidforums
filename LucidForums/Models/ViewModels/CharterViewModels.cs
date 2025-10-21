namespace LucidForums.Models.ViewModels;

public record CharterListItemVm(Guid Id, string Name, string Purpose);

public record CharterDetailsVm(Guid Id, string Name, string Purpose, IReadOnlyList<string> Rules, IReadOnlyList<string> Behaviors);

// Used for both Create and Edit
public class CharterEditVm
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;

    // Multiline inputs will be split/joined by new lines in controller mapping
    public string RulesMultiline { get; set; } = string.Empty;
    public string BehaviorsMultiline { get; set; } = string.Empty;
}
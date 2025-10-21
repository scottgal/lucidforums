using System.ComponentModel.DataAnnotations;

namespace LucidForums.Models.ViewModels;

public class CreateForumVm
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Description { get; set; }

    [Display(Name = "Slug (optional)")]
    [RegularExpression("^[a-z0-9-]*$", ErrorMessage = "Slug can only contain lowercase letters, numbers, and hyphens.")]
    [StringLength(120)]
    public string? Slug { get; set; }
}
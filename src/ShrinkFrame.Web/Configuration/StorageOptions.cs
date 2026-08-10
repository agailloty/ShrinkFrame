using System.ComponentModel.DataAnnotations;

namespace ShrinkFrame.Web.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required]
    public string WorkRoot { get; init; } = "/data/work";

    [Range(1, 100)]
    public int MaximumFileSizeGigabytes { get; init; } = 20;
}

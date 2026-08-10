using System.ComponentModel.DataAnnotations;

namespace ShrinkFrame.Web.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required]
    public string WorkRoot { get; init; } = "/data/work";

    [Range(1, 100)]
    public int MaximumFileSizeGigabytes { get; init; } = 20;

    [Range(0, long.MaxValue)]
    public long ReserveBytes { get; init; } = 5L * 1024 * 1024 * 1024;

    [Range(4096, 1048576)]
    public int BufferSizeBytes { get; init; } = 128 * 1024;
}

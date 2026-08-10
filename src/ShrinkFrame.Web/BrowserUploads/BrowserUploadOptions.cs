using System.ComponentModel.DataAnnotations;

namespace ShrinkFrame.Web.BrowserUploads;

public sealed class BrowserUploadOptions
{
    public const string SectionName = "BrowserUploads";

    [Range(1, long.MaxValue)]
    public long MaximumFileSizeBytes { get; init; } = 20L * 1024 * 1024 * 1024;

    [Range(4096, 1048576)]
    public int BufferSizeBytes { get; init; } = 128 * 1024;

    [MinLength(1)]
    public string[] AllowedOrigins { get; init; } = [];

}

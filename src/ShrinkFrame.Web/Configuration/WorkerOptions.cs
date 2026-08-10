using System.ComponentModel.DataAnnotations;

namespace ShrinkFrame.Web.Configuration;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    [Range(1, 16)]
    public int CompressionConcurrency { get; init; } = 1;

    [Range(1, 16)]
    public int AcquisitionConcurrency { get; init; } = 2;
}

namespace ShrinkFrame.Infrastructure.Storage;

public sealed class WorkStorageOptions
{
    public required string WorkRoot { get; init; }
    public long ReserveBytes { get; init; }
    public int BufferSizeBytes { get; init; } = 128 * 1024;
}

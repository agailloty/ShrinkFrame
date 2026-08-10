using ShrinkFrame.Application;

namespace ShrinkFrame.Infrastructure.Storage;

public sealed class DiskCapacityService(IStorageCapacityReporter reporter, WorkStorageOptions options) : IDiskCapacityService
{
    public CapacityAdmission Evaluate(long sourceBytes, bool forceRequested = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceBytes);
        var capacity = reporter.GetCapacity();
        try
        {
            var workingBytes = checked((long)Math.Ceiling(sourceBytes * 2.2m));
            var required = checked(workingBytes + options.ReserveBytes);
            var reason = required <= capacity.AvailableBytes ? CapacityReason.Sufficient : CapacityReason.InsufficientSpace;
            return new(sourceBytes, required, capacity.AvailableBytes, options.ReserveBytes, reason, forceRequested);
        }
        catch (OverflowException)
        {
            return new(sourceBytes, long.MaxValue, capacity.AvailableBytes, options.ReserveBytes,
                CapacityReason.ArithmeticOverflow, forceRequested);
        }
    }
}

public sealed class LocalStorageCapacityReporter : IStorageCapacityReporter
{
    private readonly string root;

    public LocalStorageCapacityReporter(WorkStorageOptions options) => root = Path.GetFullPath(options.WorkRoot);

    public StorageCapacity GetCapacity()
    {
        var drive = new DriveInfo(Path.GetPathRoot(root) ?? root);
        return new(drive.TotalSize, drive.AvailableFreeSpace);
    }
}

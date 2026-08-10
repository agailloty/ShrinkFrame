namespace ShrinkFrame.Domain;

public readonly record struct ConnectionId(Guid Value)
{
    public static ConnectionId New() => new(Guid.NewGuid());
    public static ConnectionId From(Guid value) => value == Guid.Empty ? throw Error() : new(value);
    private static DomainException Error() => new(DomainErrors.InvalidIdentifier, "Connection ID cannot be empty.");
}

public readonly record struct BatchId(Guid Value)
{
    public static BatchId New() => new(Guid.NewGuid());
    public static BatchId From(Guid value) => value == Guid.Empty ? throw Error() : new(value);
    private static DomainException Error() => new(DomainErrors.InvalidIdentifier, "Batch ID cannot be empty.");
}

public readonly record struct JobId(Guid Value)
{
    public static JobId New() => new(Guid.NewGuid());
    public static JobId From(Guid value) => value == Guid.Empty ? throw Error() : new(value);
    private static DomainException Error() => new(DomainErrors.InvalidIdentifier, "Job ID cannot be empty.");
}

public readonly record struct PresetId
{
    public PresetId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException(DomainErrors.InvalidIdentifier, "Preset ID is required.");
        Value = value;
    }
    public string Value { get; }
    public override string ToString() => Value;
}

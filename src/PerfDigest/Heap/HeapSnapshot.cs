namespace PerfDigest.Heap;

public sealed class HeapSnapshot
{
    public required string File { get; init; }

    public required string Runtime { get; init; }

    public bool LiveOnly { get; init; }

    public SnapshotFormat Format { get; init; }

    public DateTime? Time { get; init; }

    public DateTime? CollectedTime { get; init; }

    public string? Origin { get; init; }

    public bool HasDelegates { get; init; }

    public float? SamplingMultiplier { get; init; }

    public Dictionary<string, TypeStat> Types { get; } = [];

    public Dictionary<string, long> DelegateTargets { get; } = [];

    public Dictionary<string, string> Modules { get; } = [];

    public long FinalizableCount { get; set; }

    public long ObjectCount => Types.Values.Sum(t => t.Count);

    public long TotalSize => Types.Values.Sum(t => t.Size);
}

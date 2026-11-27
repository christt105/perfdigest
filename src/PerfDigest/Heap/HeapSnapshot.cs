namespace PerfDigest.Heap;

public sealed class HeapSnapshot
{
    public required string File { get; init; }

    public required string Runtime { get; init; }

    public bool LiveOnly { get; init; }

    public Dictionary<string, TypeStat> Types { get; } = [];

    public Dictionary<string, long> DelegateTargets { get; } = [];

    public long FinalizableCount { get; set; }

    public long ObjectCount => Types.Values.Sum(t => t.Count);

    public long TotalSize => Types.Values.Sum(t => t.Size);
}

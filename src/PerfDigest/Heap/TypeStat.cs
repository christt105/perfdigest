namespace PerfDigest.Heap;

public sealed class TypeStat
{
    public long Count { get; set; }

    public long Size { get; set; }

    public List<ulong> Samples { get; } = [];
}

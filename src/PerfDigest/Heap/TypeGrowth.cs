namespace PerfDigest.Heap;

public sealed record TypeGrowth(string Name, long[] Counts, long[] Sizes, bool IsOwn)
{
    public long CountDelta => Counts[^1] - Counts[0];

    public long SizeDelta => Sizes[^1] - Sizes[0];

    public bool GrowsEveryStep => Counts.Zip(Counts.Skip(1)).All(p => p.Second > p.First);
}

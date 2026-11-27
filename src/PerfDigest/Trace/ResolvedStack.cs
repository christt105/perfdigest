namespace PerfDigest.Trace;

public sealed record ResolvedStack(int[] RootToLeaf)
{
    public static readonly ResolvedStack Empty = new([]);
}

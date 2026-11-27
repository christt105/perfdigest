namespace PerfDigest.Heap;

public interface IRetentionGraph : IDisposable
{
    string Describe(ulong sample);

    IReadOnlyList<string> Path(ulong sample);
}

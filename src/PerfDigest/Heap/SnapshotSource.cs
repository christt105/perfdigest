namespace PerfDigest.Heap;

public sealed record SnapshotSource(string Path, string Label, SnapshotFormat Format, DateTime? Time, string? Origin);

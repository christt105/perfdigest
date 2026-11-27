using PerfDigest.Cli;

namespace PerfDigest.Heap;

public static class SnapshotResolver
{
    public static IReadOnlyList<SnapshotSource> Resolve(IReadOnlyList<string> inputs, List<string> notes)
    {
        var sources = new List<SnapshotSource>();
        foreach (var input in inputs)
        {
            if (input.EndsWith(".diagsession", StringComparison.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(input);
                var snapshots = DiagSessionReader.Extract(input, notes);
                sources.AddRange(snapshots.Select((s, i) => new SnapshotSource(
                    s.ExtractedPath,
                    snapshots.Count == 1 ? name : $"{name} #{i + 1}",
                    SnapshotFormat.GcDump,
                    s.Time,
                    $"`{s.EntryName}`, time from {s.TimeSource}")));
            }
            else
            {
                var format = input.EndsWith(".gcdump", StringComparison.OrdinalIgnoreCase) ? SnapshotFormat.GcDump : SnapshotFormat.Dump;
                sources.Add(new SnapshotSource(input, Path.GetFileName(input), format, null, null));
            }
        }

        if (sources.Count == 0)
        {
            throw new UsageException("No heap snapshot found in the input.");
        }

        if (sources.Select(s => s.Format).Distinct().Count() > 1)
        {
            throw new UsageException("Do not mix memory dumps (.dmp) with .gcdump/.diagsession snapshots: they name and count types differently.");
        }

        return sources;
    }
}

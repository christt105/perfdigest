using PerfDigest.Markdown;

namespace PerfDigest.Heap;

public static class HeapCommand
{
    public static string Run(HeapOptions options)
    {
        var classifier = new CodeClassifier(options.Focus);
        var notes = new List<string>();
        var sources = SnapshotResolver.Resolve(options.Inputs, notes);
        var snapshots = new List<HeapSnapshot>();
        IRetentionGraph? graph = null;
        try
        {
            for (var i = 0; i < sources.Count; i++)
            {
                var keepGraph = i == sources.Count - 1 && sources.Count > 1 && options.Roots > 0;
                var (snapshot, g) = sources[i].Format == SnapshotFormat.GcDump
                    ? GcDumpScanner.Scan(sources[i], keepGraph)
                    : HeapScanner.Scan(sources[i], options.IncludeDead, keepGraph);
                snapshots.Add(snapshot);
                graph = g ?? graph;
            }

            var gcDump = snapshots[0].Format == SnapshotFormat.GcDump;
            if (gcDump && options.IncludeDead)
            {
                notes.Add("`--include-dead` is ignored for .gcdump: the snapshot only stores objects reachable from roots.");
            }

            foreach (var snapshot in snapshots.Where(s => s.Origin is not null))
            {
                notes.Add($"{snapshot.File}: {snapshot.Origin}");
            }

            var times = snapshots.Select(s => s.CollectedTime).ToList();
            if (times.All(t => t is not null) && times.Zip(times.Skip(1)).Any(p => p.Second < p.First))
            {
                notes.Add($"⚠ By the collection time stored inside each snapshot ({string.Join(", ", times.Select(t => t!.Value.ToString("HH:mm:ss")))}) this order is not chronological. Pass them oldest first, or the growth columns are reversed.");
            }

            var md = new MarkdownBuilder();
            md.Heading(1, snapshots.Count == 1 ? $"Heap digest: {snapshots[0].File}" : $"Heap growth digest: {snapshots.Count} snapshots");
            md.Bullet(options.IncludeDead && !gcDump ? "Counting every object on the heap, including unreachable garbage." : "Counting only objects reachable from GC roots (live).");
            md.Bullet(options.Focus.Count > 0
                ? $"\"Your types\" = names starting with: {string.Join(", ", options.Focus)}"
                : "\"Your types\" = everything outside System.*, Microsoft.* and similar framework namespaces (pass --focus to narrow it)");
            if (gcDump)
            {
                md.Bullet("Source: .gcdump snapshots (Visual Studio Memory Usage, dotnet-gcdump or PerfView). Sizes are each object's own size, as in VS's Size (Bytes) column; delegate targets and field names are not stored in this format.");
            }

            foreach (var note in notes)
            {
                md.Bullet(note);
            }

            md.Line();
            if (snapshots.Count > 1)
            {
                md.Line("> Reading guide: snapshots are in chronological order and should come from the same process run, taken after repeating the same user action. A type of yours whose count grows by about the number of repetitions is leaking; the retention path shows who holds it.");
            }

            md.Line();
            HeapReport.Overview(md, snapshots);
            if (snapshots.Count == 1)
            {
                HeapReport.Single(md, snapshots[0], classifier, options);
                return md.ToString();
            }

            var growth = HeapReport.Diff(md, snapshots, classifier, options);
            if (graph is not null)
            {
                var last = snapshots[^1];
                var candidates = growth.Where(g => g.IsOwn && g.CountDelta > 0).OrderByDescending(g => g.GrowsEveryStep).ThenByDescending(g => g.CountDelta).ToList();
                if (candidates.Count == 0)
                {
                    candidates = growth.Where(g => g.SizeDelta > 0).OrderByDescending(g => g.SizeDelta).ToList();
                }

                Cli.Progress.Info("Computing retention paths...");
                HeapReport.Paths(md, graph, !gcDump, candidates.Take(options.Roots)
                    .Select(c => (c.Name, (IReadOnlyList<ulong>)(last.Types.GetValueOrDefault(c.Name)?.Samples ?? []))));
            }

            return md.ToString();
        }
        finally
        {
            graph?.Dispose();
        }
    }
}

using PerfDigest.Markdown;

namespace PerfDigest.Heap;

public static class HeapCommand
{
    public static string Run(HeapOptions options)
    {
        var classifier = new CodeClassifier(options.Focus);
        var snapshots = new List<HeapSnapshot>();
        RetentionGraph? graph = null;
        try
        {
            for (var i = 0; i < options.Dumps.Count; i++)
            {
                var isLast = i == options.Dumps.Count - 1;
                var (snapshot, g) = HeapScanner.Scan(options.Dumps[i], options.IncludeDead, keepGraph: isLast && options.Dumps.Count > 1);
                snapshots.Add(snapshot);
                graph = g ?? graph;
            }

            var md = new MarkdownBuilder();
            md.Heading(1, snapshots.Count == 1 ? $"Heap digest: {snapshots[0].File}" : $"Heap growth digest: {snapshots.Count} dumps");
            md.Bullet(options.IncludeDead ? "Counting every object on the heap, including unreachable garbage." : "Counting only objects reachable from GC roots (live).");
            md.Bullet(options.Focus.Count > 0
                ? $"\"Your types\" = names starting with: {string.Join(", ", options.Focus)}"
                : "\"Your types\" = everything outside System.*, Microsoft.* and similar framework namespaces (pass --focus to narrow it)");
            md.Line();
            if (snapshots.Count > 1)
            {
                md.Line("> Reading guide: dumps are in chronological order and should come from the same process run, taken after repeating the same user action. A type of yours whose count grows by about the number of repetitions is leaking; the retention path shows who holds it.");
            }

            md.Line();
            HeapReport.Overview(md, snapshots);
            if (snapshots.Count == 1)
            {
                HeapReport.Single(md, snapshots[0], classifier, options);
                return md.ToString();
            }

            var growth = HeapReport.Diff(md, snapshots, classifier, options);
            if (graph is not null && options.Roots > 0)
            {
                var last = snapshots[^1];
                var candidates = growth.Where(g => g.IsOwn && g.CountDelta > 0).OrderByDescending(g => g.GrowsEveryStep).ThenByDescending(g => g.CountDelta).ToList();
                if (candidates.Count == 0)
                {
                    candidates = growth.Where(g => g.SizeDelta > 0).OrderByDescending(g => g.SizeDelta).ToList();
                }

                Cli.Progress.Info("Computing retention paths...");
                HeapReport.Paths(md, graph, candidates.Take(options.Roots)
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

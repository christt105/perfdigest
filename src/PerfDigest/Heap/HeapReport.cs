using PerfDigest.Markdown;

namespace PerfDigest.Heap;

public static class HeapReport
{
    public static void Overview(MarkdownBuilder md, IReadOnlyList<HeapSnapshot> snapshots)
    {
        md.Table(["Dump", "Runtime", "Objects", "Size", "Finalizable"], snapshots
            .Select(s => (IReadOnlyList<string>)[s.File, s.Runtime, s.ObjectCount.ToString("N0"), MarkdownBuilder.Bytes(s.TotalSize), s.FinalizableCount.ToString("N0")]));
    }

    public static void Single(MarkdownBuilder md, HeapSnapshot snapshot, CodeClassifier classifier, HeapOptions options)
    {
        md.Heading(2, "Largest types");
        md.Table(["Type", "Count", "Size", "%"], snapshot.Types.OrderByDescending(t => t.Value.Size).Take(options.Top)
            .Select(t => (IReadOnlyList<string>)[NameShortener.Type(t.Key), t.Value.Count.ToString("N0"), MarkdownBuilder.Bytes(t.Value.Size), MarkdownBuilder.Percent(t.Value.Size, snapshot.TotalSize)]));

        md.Heading(2, "Your types by instance count");
        md.Table(["Type", "Count", "Size"], snapshot.Types.Where(t => classifier.IsOwnType(t.Key)).OrderByDescending(t => t.Value.Count).Take(options.Top)
            .Select(t => (IReadOnlyList<string>)[NameShortener.Type(t.Key), t.Value.Count.ToString("N0"), MarkdownBuilder.Bytes(t.Value.Size)]));

        md.Heading(2, "Delegate targets");
        md.Line("Objects kept alive by delegates (event subscriptions, callbacks). Large counts of your types here are typical event-handler leaks.");
        md.Line();
        md.Table(["Target and method", "Count"], snapshot.DelegateTargets.OrderByDescending(d => d.Value).Take(options.Top)
            .Select(d => (IReadOnlyList<string>)[NameShortener.Type(d.Key), d.Value.ToString("N0")]));
    }

    public static IReadOnlyList<TypeGrowth> Diff(MarkdownBuilder md, IReadOnlyList<HeapSnapshot> snapshots, CodeClassifier classifier, HeapOptions options)
    {
        var growth = snapshots.SelectMany(s => s.Types.Keys).Distinct()
            .Select(name => new TypeGrowth(
                name,
                snapshots.Select(s => s.Types.GetValueOrDefault(name)?.Count ?? 0).ToArray(),
                snapshots.Select(s => s.Types.GetValueOrDefault(name)?.Size ?? 0).ToArray(),
                classifier.IsOwnType(name)))
            .ToList();
        var steps = snapshots.Count > 2 ? " (✔ = grew in every step)" : "";

        md.Heading(2, "Your types that grew");
        md.Line($"Main leak candidates: instances of your own types that are still reachable and increased between dumps{steps}.");
        md.Line();
        GrowthTable(md, growth.Where(g => g.IsOwn && g.CountDelta > 0).OrderByDescending(g => g.GrowsEveryStep).ThenByDescending(g => g.CountDelta), snapshots.Count, options.Top);

        md.Heading(2, "All types by size growth");
        md.Line("Includes framework types (strings, arrays, collections) that usually belong to the leaking objects above.");
        md.Line();
        GrowthTable(md, growth.Where(g => g.SizeDelta > 0).OrderByDescending(g => g.SizeDelta), snapshots.Count, options.Top);

        md.Heading(2, "Delegate targets that grew");
        md.Line("Each row is \"object kept alive . handler method via delegate type\". Growth here usually means `+=` without `-=` on an event that lives longer than the subscriber.");
        md.Line();
        var keys = snapshots.SelectMany(s => s.DelegateTargets.Keys).Distinct();
        md.Table(["Target and method", "Counts", "Δ count"], keys
            .Select(k => (Key: k, Counts: snapshots.Select(s => s.DelegateTargets.GetValueOrDefault(k)).ToArray()))
            .Where(k => k.Counts[^1] > k.Counts[0])
            .OrderByDescending(k => k.Counts[^1] - k.Counts[0]).Take(options.Top)
            .Select(k => (IReadOnlyList<string>)[NameShortener.Type(k.Key), string.Join(" → ", k.Counts.Select(c => c.ToString("N0"))), MarkdownBuilder.Signed(k.Counts[^1] - k.Counts[0])]));

        md.Heading(2, "Types that shrank the most");
        md.Line("For context: what was released between the first and the last dump.");
        md.Line();
        GrowthTable(md, growth.Where(g => g.SizeDelta < 0).OrderBy(g => g.SizeDelta), snapshots.Count, Math.Min(10, options.Top));
        return growth;
    }

    public static void Paths(MarkdownBuilder md, RetentionGraph graph, IEnumerable<(string Type, IReadOnlyList<ulong> Samples)> targets)
    {
        md.Heading(2, "Why they stay alive (retention paths)");
        md.Line("Shortest path from a GC root (or a static field) to one instance, in the last dump. Look for the first link that should have been removed: an event, a static/cached collection, a timer or a parent that outlives the object.");
        md.Line();
        foreach (var (type, samples) in targets)
        {
            var address = samples.FirstOrDefault();
            if (address == 0)
            {
                continue;
            }

            md.Line($"**{NameShortener.Type(type)}** (instance 0x{address:x}):");
            md.Code(graph.Path(address));
        }
    }

    private static void GrowthTable(MarkdownBuilder md, IEnumerable<TypeGrowth> rows, int dumps, int top)
    {
        md.Table(["Type", "Counts", "Δ count", "Δ size", "Size now"], rows.Take(top)
            .Select(g => (IReadOnlyList<string>)
            [
                NameShortener.Type(g.Name) + (dumps > 2 && g.GrowsEveryStep ? " ✔" : ""),
                string.Join(" → ", g.Counts.Select(c => c.ToString("N0"))),
                MarkdownBuilder.Signed(g.CountDelta),
                MarkdownBuilder.SignedBytes(g.SizeDelta),
                MarkdownBuilder.Bytes(g.Sizes[^1]),
            ]));
    }
}

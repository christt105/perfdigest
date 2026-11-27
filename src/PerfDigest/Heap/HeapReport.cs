using PerfDigest.Markdown;

namespace PerfDigest.Heap;

public static class HeapReport
{
    public static void Overview(MarkdownBuilder md, IReadOnlyList<HeapSnapshot> snapshots)
    {
        var withTime = snapshots.Any(s => s.Time is not null);
        IReadOnlyList<string> headers = withTime
            ? ["#", "Snapshot", "Taken (UTC)", "Source", "Objects", "Size"]
            : ["#", "Snapshot", "Source", "Objects", "Size"];
        md.Table(headers, snapshots.Select((s, i) =>
        {
            var source = s.Format == SnapshotFormat.Dump ? $"{s.Runtime}, {s.FinalizableCount:N0} finalizable" : s.Runtime;
            if (s.SamplingMultiplier is { } multiplier)
            {
                source += $", sampled (×{multiplier:0.##} avg, counts scaled)";
            }

            var cells = new List<string> { (i + 1).ToString(), s.File };
            if (withTime)
            {
                cells.Add(s.Time?.ToString("yyyy-MM-dd HH:mm:ss") ?? "?");
            }

            cells.AddRange([source, s.ObjectCount.ToString("N0"), MarkdownBuilder.Bytes(s.TotalSize)]);
            return (IReadOnlyList<string>)cells;
        }));
    }

    public static void Single(MarkdownBuilder md, HeapSnapshot snapshot, CodeClassifier classifier, HeapOptions options)
    {
        md.Heading(2, "Largest types");
        md.Table(["Type", "Count", "Size", "%"], snapshot.Types.OrderByDescending(t => t.Value.Size).Take(options.Top)
            .Select(t => (IReadOnlyList<string>)[NameShortener.Type(t.Key), t.Value.Count.ToString("N0"), MarkdownBuilder.Bytes(t.Value.Size), MarkdownBuilder.Percent(t.Value.Size, snapshot.TotalSize)]));

        md.Heading(2, "Your types by instance count");
        md.Table(["Type", "Count", "Size"], snapshot.Types.Where(t => classifier.IsOwnType(t.Key, snapshot.Modules.GetValueOrDefault(t.Key))).OrderByDescending(t => t.Value.Count).Take(options.Top)
            .Select(t => (IReadOnlyList<string>)[NameShortener.Type(t.Key), t.Value.Count.ToString("N0"), MarkdownBuilder.Bytes(t.Value.Size)]));

        if (!snapshot.HasDelegates)
        {
            return;
        }

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
                classifier.IsOwnType(name, snapshots.Select(s => s.Modules.GetValueOrDefault(name)).FirstOrDefault(m => m is not null))))
            .ToList();
        var steps = snapshots.Count > 2 ? " (✔ = grew in every step)" : "";

        md.Heading(2, "Your types that grew");
        md.Line($"Main leak candidates: instances of your own types that are still reachable and increased between snapshots{steps}.");
        md.Line();
        GrowthTable(md, growth.Where(g => g.IsOwn && g.CountDelta > 0).OrderByDescending(g => g.GrowsEveryStep).ThenByDescending(g => g.CountDelta), snapshots.Count, options.Top);

        md.Heading(2, "All types by size growth");
        md.Line("Includes framework types (strings, arrays, collections) that usually belong to the leaking objects above.");
        md.Line();
        GrowthTable(md, growth.Where(g => g.SizeDelta > 0).OrderByDescending(g => g.SizeDelta), snapshots.Count, options.Top);

        GrowingEveryStep(md, growth, snapshots.Count, options.Top);
        Steps(md, snapshots, growth, options.Top);

        if (snapshots.All(s => s.HasDelegates))
        {
            DelegateGrowth(md, snapshots, options.Top);
        }

        md.Heading(2, "Types that shrank the most");
        md.Line("For context: what was released between the first and the last snapshot.");
        md.Line();
        GrowthTable(md, growth.Where(g => g.SizeDelta < 0).OrderBy(g => g.SizeDelta), snapshots.Count, Math.Min(10, options.Top));
        return growth;
    }

    private static void GrowingEveryStep(MarkdownBuilder md, IReadOnlyList<TypeGrowth> growth, int count, int top)
    {
        if (count < 3)
        {
            return;
        }

        md.Heading(2, "Types that grew in every step");
        md.Line("Every type, framework ones included, whose instance count went up between each pair of consecutive snapshots, by count growth. A steady increase per step that matches the number of repetitions is the classic leak signature.");
        md.Line();
        GrowthTable(md, growth.Where(g => g.GrowsEveryStep).OrderByDescending(g => g.CountDelta), count, top);
    }

    private static void Steps(MarkdownBuilder md, IReadOnlyList<HeapSnapshot> snapshots, IReadOnlyList<TypeGrowth> growth, int top)
    {
        md.Heading(2, "Each step against the previous snapshot");
        md.Line("Same numbers as Visual Studio's Memory Usage comparison (snapshot N, baseline N-1): Count Diff and Size Diff per type, largest count growth first.");
        var rows = Math.Min(10, top);
        for (var step = 1; step < snapshots.Count; step++)
        {
            var i = step;
            md.Line();
            md.Line($"**#{i} → #{i + 1}** ({snapshots[i - 1].File} → {snapshots[i].File}): {MarkdownBuilder.Signed(snapshots[i].ObjectCount - snapshots[i - 1].ObjectCount)} objects, {MarkdownBuilder.SignedBytes(snapshots[i].TotalSize - snapshots[i - 1].TotalSize)}");
            md.Line();
            md.Table(["Type", "Count", "Count diff", "Size (bytes)", "Size diff (bytes)"], growth
                .Where(g => g.Counts[i] > g.Counts[i - 1])
                .OrderByDescending(g => g.Counts[i] - g.Counts[i - 1]).ThenByDescending(g => g.Sizes[i] - g.Sizes[i - 1])
                .Take(rows)
                .Select(g => (IReadOnlyList<string>)
                [
                    NameShortener.Type(g.Name),
                    g.Counts[i].ToString("N0"),
                    MarkdownBuilder.Signed(g.Counts[i] - g.Counts[i - 1]),
                    g.Sizes[i].ToString("N0"),
                    MarkdownBuilder.Signed(g.Sizes[i] - g.Sizes[i - 1]),
                ]));
        }
    }

    private static void DelegateGrowth(MarkdownBuilder md, IReadOnlyList<HeapSnapshot> snapshots, int top)
    {
        md.Heading(2, "Delegate targets that grew");
        md.Line("Each row is \"object kept alive . handler method via delegate type\". Growth here usually means `+=` without `-=` on an event that lives longer than the subscriber.");
        md.Line();
        var keys = snapshots.SelectMany(s => s.DelegateTargets.Keys).Distinct();
        md.Table(["Target and method", "Counts", "Δ count"], keys
            .Select(k => (Key: k, Counts: snapshots.Select(s => s.DelegateTargets.GetValueOrDefault(k)).ToArray()))
            .Where(k => k.Counts[^1] > k.Counts[0])
            .OrderByDescending(k => k.Counts[^1] - k.Counts[0]).Take(top)
            .Select(k => (IReadOnlyList<string>)[NameShortener.Type(k.Key), string.Join(" → ", k.Counts.Select(c => c.ToString("N0"))), MarkdownBuilder.Signed(k.Counts[^1] - k.Counts[0])]));
    }

    public static void Paths(MarkdownBuilder md, IRetentionGraph graph, bool fieldNames, IEnumerable<(string Type, IReadOnlyList<ulong> Samples)> targets)
    {
        md.Heading(2, "Why they stay alive (retention paths)");
        md.Line(fieldNames
            ? "Shortest path from a GC root (or a static field) to one instance, in the last snapshot. Look for"
              + " the first link that should have been removed: an event, a static/cached collection, a timer or a parent that outlives the object."
            : "Shortest path from a GC root to one instance, in the last snapshot (.gcdump stores references but not field names). Look for"
              + " the first link that should have been removed: an event, a static/cached collection, a timer or a parent that outlives the object.");
        md.Line();
        foreach (var (type, samples) in targets)
        {
            if (samples.Count == 0)
            {
                continue;
            }

            md.Line($"**{NameShortener.Type(type)}** ({graph.Describe(samples[0])}):");
            md.Code(graph.Path(samples[0]));
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

using PerfDigest.Markdown;

namespace PerfDigest.Trace;

public static class CpuSection
{
    private const int BreakdownMethods = 5;
    private const int BreakdownCallees = 8;

    public static void Write(MarkdownBuilder md, TraceData data, StackCatalog catalog, TraceOptions options)
    {
        var total = data.TotalSamples;
        md.Heading(2, "Time: where the samples go");
        if (total == 0)
        {
            md.Line("No CPU/thread samples for this process. In Visual Studio enable the *CPU Usage* tool; with dotnet-trace use the default profile.");
            return;
        }

        var ms = data.SampleIntervalMs;
        md.Bullet($"Samples: {total:N0}, kind: {data.SampleKind}; ~{MarkdownBuilder.Number(total * ms, 0)} ms at {MarkdownBuilder.Number(ms, 3)} ms/sample")
            .Line();

        var exclusive = new Dictionary<int, long>();
        var attributed = new Dictionary<int, long>();
        var inclusive = new Dictionary<int, long>();
        var paths = new Dictionary<string, long>();
        var threads = new Dictionary<int, long>();
        var threadTop = new Dictionary<(int Thread, int Frame), long>();
        var callees = new Dictionary<int, Dictionary<int, long>>();
        long noOwn = 0;

        foreach (var (key, count) in data.Samples)
        {
            var stack = catalog.Resolve(key.Stack);
            threads[key.ThreadId] = threads.GetValueOrDefault(key.ThreadId) + count;
            var path = PathFormatter.Collapsed(stack, catalog);
            paths[path] = paths.GetValueOrDefault(path) + count;
            if (stack.RootToLeaf.Length == 0)
            {
                noOwn += count;
                continue;
            }

            Bump(exclusive, stack.RootToLeaf[^1], count);
            var deepest = PathFormatter.DeepestOwn(stack, catalog);
            if (deepest < 0)
            {
                noOwn += count;
            }
            else
            {
                var frame = stack.RootToLeaf[deepest];
                Bump(attributed, frame, count);
                threadTop[(key.ThreadId, frame)] = threadTop.GetValueOrDefault((key.ThreadId, frame)) + count;
            }

            var seen = new HashSet<int>();
            for (var i = 0; i < stack.RootToLeaf.Length; i++)
            {
                var id = stack.RootToLeaf[i];
                if (!catalog.Frame(id).IsOwn || !seen.Add(id))
                {
                    continue;
                }

                Bump(inclusive, id, count);
                var callee = i + 1 < stack.RootToLeaf.Length ? stack.RootToLeaf[i + 1] : -1;
                if (!callees.TryGetValue(id, out var map))
                {
                    map = [];
                    callees[id] = map;
                }

                Bump(map, callee, count);
            }
        }

        string Ms(long samples) => MarkdownBuilder.Number(samples * ms, 0);
        string Pct(long samples) => MarkdownBuilder.Percent(samples, total);

        md.Heading(3, "Your code: time attributed to each method");
        md.Line("Self time plus time in framework/OS code it calls directly (deepest frame of your code on each sample). This is the best \"what to optimize\" list.");
        md.Line();
        md.Table(["Method", "ms", "%"], attributed.OrderByDescending(p => p.Value).Take(options.Top)
            .Select(p => (IReadOnlyList<string>)[catalog.Frame(p.Key).Display, Ms(p.Value), Pct(p.Value)])
            .Append(["(samples with none of your code on the stack)", Ms(noOwn), Pct(noOwn)]));

        md.Heading(3, "Your code: inclusive time");
        md.Line("Time with the method anywhere on the stack (itself and everything below it).");
        md.Line();
        md.Table(["Method", "ms", "%"], inclusive.OrderByDescending(p => p.Value).Take(options.Top)
            .Select(p => (IReadOnlyList<string>)[catalog.Frame(p.Key).Display, Ms(p.Value), Pct(p.Value)]));

        md.Heading(3, "Breakdown of the heaviest methods");
        foreach (var (id, inc) in inclusive.OrderByDescending(p => p.Value).Take(BreakdownMethods))
        {
            md.Line($"**{catalog.Frame(id).Display}** ({Ms(inc)} ms inclusive) calls:");
            md.Line();
            md.Table(["Callee", "ms", "% of method"], callees[id].OrderByDescending(p => p.Value).Take(BreakdownCallees)
                .Select(p => (IReadOnlyList<string>)[p.Key < 0 ? "(self)" : catalog.Frame(p.Key).Display, Ms(p.Value), MarkdownBuilder.Percent(p.Value, inc)]));
        }

        md.Heading(3, "Hot paths");
        md.Line("Most frequent call chains, keeping only your frames (` > `) and the final framework/OS leaf (` → `).");
        md.Line();
        md.Code(paths.OrderByDescending(p => p.Value).Take(options.Paths)
            .Select(p => $"{Ms(p.Value),8} ms {Pct(p.Value),6}  {p.Key}"));

        md.Heading(3, "Self time, any code");
        md.Line("Leaf frames, including framework/OS. Useful to spot GC, locks, I/O, rendering or JIT showing up.");
        md.Line();
        md.Table(["Frame", "ms", "%"], exclusive.OrderByDescending(p => p.Value).Take(options.Top)
            .Select(p => (IReadOnlyList<string>)[catalog.Frame(p.Key).Display, Ms(p.Value), Pct(p.Value)]));

        md.Heading(3, "Threads");
        md.Table(["Thread", "ms", "%", "Top method of your code"], threads.OrderByDescending(p => p.Value).Take(10)
            .Select(p =>
            {
                var top = threadTop.Where(t => t.Key.Thread == p.Key).OrderByDescending(t => t.Value).FirstOrDefault();
                var name = data.ThreadNames.GetValueOrDefault(p.Key);
                return (IReadOnlyList<string>)
                [
                    string.IsNullOrEmpty(name) ? p.Key.ToString() : $"{p.Key} {name}",
                    Ms(p.Value), Pct(p.Value),
                    top.Value == 0 ? "" : $"{catalog.Frame(top.Key.Frame).Display} ({Ms(top.Value)} ms)",
                ];
            }));
    }

    private static void Bump(Dictionary<int, long> map, int key, long count) => map[key] = map.GetValueOrDefault(key) + count;
}

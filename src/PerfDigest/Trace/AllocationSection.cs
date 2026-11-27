using PerfDigest.Markdown;

namespace PerfDigest.Trace;

public static class AllocationSection
{
    public static void Write(MarkdownBuilder md, TraceData data, StackCatalog catalog, TraceOptions options)
    {
        md.Heading(2, "Allocations");
        if (data.Allocations.Count == 0)
        {
            md.Line("No allocation events. In Visual Studio use the *.NET Object Allocation Tracking* tool; with dotnet-trace use `--profile gc-verbose`.");
            return;
        }

        var total = data.Allocations.Values.Sum(t => t.Amount);
        md.Line($"Sampled allocation events (about one per 100 KB allocated): {data.Allocations.Values.Sum(t => t.Count):N0}, ~{MarkdownBuilder.Bytes(total)} allocated in {MarkdownBuilder.Number(data.DurationMs / 1000, 1)} s. Sizes are estimates.");
        md.Line();

        md.Heading(3, "By type");
        md.Table(["Type", "Size", "%"], data.Allocations
            .GroupBy(p => p.Key.TypeName)
            .Select(g => (Type: g.Key, Bytes: g.Sum(p => p.Value.Amount)))
            .OrderByDescending(t => t.Bytes).Take(options.Top)
            .Select(t => (IReadOnlyList<string>)[NameShortener.Type(t.Type), MarkdownBuilder.Bytes(t.Bytes), MarkdownBuilder.Percent(t.Bytes, total)]));

        md.Heading(3, "By allocating method of your code");
        md.Table(["Method", "Type", "Size", "%"], data.Allocations
            .GroupBy(p => (Frame: Allocator(p.Key, catalog), p.Key.TypeName))
            .Select(g => (g.Key.Frame, g.Key.TypeName, Bytes: g.Sum(p => p.Value.Amount)))
            .OrderByDescending(t => t.Bytes).Take(options.Top)
            .Select(t => (IReadOnlyList<string>)[t.Frame, NameShortener.Type(t.TypeName), MarkdownBuilder.Bytes(t.Bytes), MarkdownBuilder.Percent(t.Bytes, total)]));
    }

    private static string Allocator(TypedStackKey key, StackCatalog catalog)
    {
        var stack = catalog.Resolve(key.Stack);
        var deepest = PathFormatter.DeepestOwn(stack, catalog);
        return deepest < 0 ? "(no stack or no own code)" : catalog.Frame(stack.RootToLeaf[deepest]).Display;
    }
}

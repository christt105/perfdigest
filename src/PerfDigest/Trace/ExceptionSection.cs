using PerfDigest.Markdown;

namespace PerfDigest.Trace;

public static class ExceptionSection
{
    private const int MessageLength = 120;

    public static void Write(MarkdownBuilder md, TraceData data, StackCatalog catalog, TraceOptions options)
    {
        if (data.Exceptions.Count == 0)
        {
            return;
        }

        md.Heading(2, "Exceptions thrown");
        md.Line("First-chance exceptions; many of them in a loop are expensive even when caught.");
        md.Line();
        md.Table(["Type", "Count", "Thrown from (your code)", "Example message"], data.Exceptions
            .GroupBy(p => (p.Key.TypeName, Frame: Thrower(p.Key, catalog)))
            .Select(g => (g.Key.TypeName, g.Key.Frame, Count: g.Sum(p => p.Value.Count), Message: g.Select(p => p.Value.Example).FirstOrDefault(m => m is not null)))
            .OrderByDescending(t => t.Count).Take(options.Top)
            .Select(t => (IReadOnlyList<string>)[t.TypeName, t.Count.ToString("N0"), t.Frame, Trim(t.Message)]));
    }

    private static string Thrower(TypedStackKey key, StackCatalog catalog)
    {
        var stack = catalog.Resolve(key.Stack);
        var deepest = PathFormatter.DeepestOwn(stack, catalog);
        return deepest < 0 ? "(unknown)" : catalog.Frame(stack.RootToLeaf[deepest]).Display;
    }

    private static string Trim(string? message) =>
        message is null ? "" : message.Length <= MessageLength ? message : message[..MessageLength] + "…";
}

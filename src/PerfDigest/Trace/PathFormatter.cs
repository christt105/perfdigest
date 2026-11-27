namespace PerfDigest.Trace;

public static class PathFormatter
{
    private const int KeepHead = 2;
    private const int KeepTail = 6;

    public static int DeepestOwn(ResolvedStack stack, StackCatalog catalog)
    {
        for (var i = stack.RootToLeaf.Length - 1; i >= 0; i--)
        {
            if (catalog.Frame(stack.RootToLeaf[i]).IsOwn)
            {
                return i;
            }
        }

        return -1;
    }

    public static string Collapsed(ResolvedStack stack, StackCatalog catalog)
    {
        if (stack.RootToLeaf.Length == 0)
        {
            return "(no stack)";
        }

        var own = new List<string>();
        foreach (var id in stack.RootToLeaf)
        {
            var frame = catalog.Frame(id);
            if (frame.IsOwn && (own.Count == 0 || own[^1] != frame.Display))
            {
                own.Add(frame.Display);
            }
        }

        var leaf = catalog.Frame(stack.RootToLeaf[^1]);
        if (own.Count == 0)
        {
            return $"(external only) … {leaf.Display}";
        }

        if (own.Count > KeepHead + KeepTail + 1)
        {
            own = [.. own.Take(KeepHead), "…", .. own.TakeLast(KeepTail)];
        }

        var path = string.Join(" > ", own);
        return leaf.IsOwn ? path : $"{path} → {leaf.Display}";
    }
}

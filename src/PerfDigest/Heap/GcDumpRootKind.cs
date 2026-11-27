namespace PerfDigest.Heap;

public static class GcDumpRootKind
{
    public const int StrongFirst = 0;
    public const int Dependent = 1;
    public const int Weak = 2;

    public static string Label(string pseudoNode)
    {
        var inner = pseudoNode.Trim('[', ']');
        if (!long.TryParse(inner, out var value))
        {
            return inner;
        }

        return value switch
        {
            1 => "strong handle",
            2 => "pinned handle (statics live here)",
            4 => "weak short handle",
            8 => "weak long handle",
            16 => "weak ref-counted handle",
            32 => "ref-counted handle (COM/WinRT)",
            64 => "dependent handle (alive only while its owner is, e.g. ConditionalWeakTable)",
            128 => "async pinned handle",
            256 => "sized-ref handle",
            512 => "weak WinRT handle",
            0x80000001 => "stack",
            0x80000002 => "finalizer queue",
            _ => $"root kind {value}",
        };
    }

    public static int Priority(string pseudoNode)
    {
        var inner = pseudoNode.Trim('[', ']');
        if (!long.TryParse(inner, out var value))
        {
            return StrongFirst;
        }

        return value switch
        {
            64 => Dependent,
            4 or 8 or 16 or 512 => Weak,
            _ => StrongFirst,
        };
    }
}

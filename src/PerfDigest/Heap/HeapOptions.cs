using PerfDigest.Cli;

namespace PerfDigest.Heap;

public sealed record HeapOptions(IReadOnlyList<string> Inputs, int Top, IReadOnlyList<string> Focus, int Roots, bool IncludeDead)
{
    public static HeapOptions From(ParsedArgs args)
    {
        if (args.Positional.Count == 0)
        {
            throw new UsageException("heap needs at least one .dmp, .gcdump or .diagsession file.");
        }

        foreach (var dump in args.Positional.Where(d => !File.Exists(d)))
        {
            throw new UsageException($"File not found: {dump}");
        }

        return new HeapOptions(args.Positional, args.Int("top", 30), args.List("focus"), args.Int("roots", 8), args.Flag("include-dead"));
    }
}

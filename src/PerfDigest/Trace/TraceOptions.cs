using PerfDigest.Cli;

namespace PerfDigest.Trace;

public sealed record TraceOptions(
    IReadOnlyList<string> Inputs,
    string? Process,
    IReadOnlyList<string> Focus,
    int Top,
    int Paths,
    string? SymbolPath,
    double? FromMs,
    double? ToMs)
{
    public static TraceOptions From(ParsedArgs args)
    {
        if (args.Positional.Count == 0)
        {
            throw new UsageException("trace needs an input file.");
        }

        var inputs = args.Positional.SelectMany(p => p.Split(',', StringSplitOptions.RemoveEmptyEntries)).ToList();
        foreach (var input in inputs.Where(i => !File.Exists(i)))
        {
            throw new UsageException($"File not found: {input}");
        }

        return new TraceOptions(
            inputs,
            args.Value("process"),
            args.List("focus"),
            args.Int("top", 25),
            args.Int("paths", 20),
            args.Value("symbols"),
            args.Double("from"),
            args.Double("to"));
    }
}

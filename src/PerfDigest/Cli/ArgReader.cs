namespace PerfDigest.Cli;

public static class ArgReader
{
    private static readonly HashSet<string> Flags = ["include-dead"];

    public static ParsedArgs Parse(string[] args)
    {
        var positional = new List<string>();
        var named = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(args[i]);
                continue;
            }

            var name = args[i][2..];
            if (Flags.Contains(name))
            {
                named[name] = null;
                continue;
            }

            if (i + 1 >= args.Length)
            {
                throw new UsageException($"--{name} needs a value.");
            }

            named[name] = args[++i];
        }

        return new ParsedArgs(positional, named);
    }
}

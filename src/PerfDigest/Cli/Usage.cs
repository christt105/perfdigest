namespace PerfDigest.Cli;

public static class Usage
{
    public const string Text = """
        perfdigest: turn .NET profiling data into a compact Markdown report an LLM can analyze.

        perfdigest trace <file> [options]
          <file>                .diagsession, .etl (one or more, comma separated) or .nettrace
          --process <name|pid>  process to analyze (default: the one with most CPU samples)
          --focus <prefixes>    comma-separated namespace/module prefixes that count as "your code"
                                (default: everything that is not runtime/framework/OS)
          --top <n>             rows per table (default 25)
          --paths <n>           hot paths to list (default 20)
          --symbols <path>      symbol path to resolve native/precompiled frames, e.g. C:\MyApp\bin;srv*C:\Symbols
          --from <ms> --to <ms> only analyze this time window (ms from trace start)

        perfdigest heap <snapshot1> [<snapshot2> ...] [options]
          snapshots: .dmp memory dumps, .gcdump files, or a .diagsession with Visual Studio
                                Memory Usage snapshots (expanded in the order they were taken)
          one snapshot: live heap summary; two or more (oldest first): growth diff to find leaks
          --top <n>             rows per table (default 30)
          --focus <prefixes>    namespace prefixes that count as "your code" (highlighted, own table)
          --roots <n>           growing types to explain with a retention path (default 8)
          --include-dead        count unreachable objects too (default: live objects only; .dmp only)

        Common:
          --out <file>          write the report there instead of stdout
        """;
}

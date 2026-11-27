using PerfDigest.Cli;
using PerfDigest.Markdown;

namespace PerfDigest.Trace;

public static class TraceCommand
{
    public static string Run(TraceOptions options)
    {
        var input = TraceLoader.Load(options.Inputs);
        using var log = input.Log;
        var process = ProcessPicker.Pick(log, options.Process);
        Progress.Info("Collecting events...");
        var data = TraceCollector.Collect(log, process, options.FromMs, options.ToMs);
        var catalog = new StackCatalog(log, new CodeClassifier(options.Focus));
        if (options.SymbolPath is not null)
        {
            catalog.LoadSymbols(options.SymbolPath, data.Samples.Keys.Select(k => k.Stack).Distinct());
        }

        var md = new MarkdownBuilder();
        md.Heading(1, $"Performance digest: {data.ProcessName} (PID {data.ProcessId})");
        md.Bullet($"Source: {input.Description}");
        md.Bullet($"Window analyzed: {MarkdownBuilder.Number(data.StartMs, 0)} to {MarkdownBuilder.Number(data.EndMs, 0)} ms ({MarkdownBuilder.Number(data.DurationMs / 1000)} s)");
        md.Bullet(options.Focus.Count > 0
            ? $"\"Your code\" = names starting with: {string.Join(", ", options.Focus)}"
            : "\"Your code\" = everything outside System.*, Microsoft.*, the runtime and the OS (pass --focus to narrow it)");
        foreach (var note in input.Notes)
        {
            md.Bullet(note);
        }

        md.Line();
        md.Line("> Reading guide: times are estimated from samples (statistical, small values are noise). `[module]` marks framework/OS frames. Start with \"time attributed\" and \"hot paths\", then use the breakdowns to see what each heavy method spends its time on. Small methods can be missing because the JIT inlined them into their caller.");

        CpuSection.Write(md, data, catalog, options);
        GcSection.Write(md, log, data, options);
        AllocationSection.Write(md, data, catalog, options);
        ExceptionSection.Write(md, data, catalog, options);
        return md.ToString();
    }
}

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Analysis;
using Microsoft.Diagnostics.Tracing.Analysis.GC;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using PerfDigest.Markdown;

namespace PerfDigest.Trace;

public static class GcSection
{
    private const int LongestPauses = 10;
    private const int HeapTimelinePoints = 12;

    public static void Write(MarkdownBuilder md, Microsoft.Diagnostics.Tracing.Etlx.TraceLog log, TraceData data, TraceOptions options)
    {
        var source = log.Events.GetSource();
        source.NeedLoadedDotNetRuntimes();
        source.Process();
        var gcs = source.Processes().FirstOrDefault(p => p.ProcessID == data.ProcessId)?.LoadedDotNetRuntime()?.GC.GCs
            .Where(gc => gc.StartRelativeMSec >= (options.FromMs ?? 0) && gc.StartRelativeMSec <= (options.ToMs ?? double.MaxValue))
            .ToList() ?? [];

        md.Heading(2, "Garbage collector");
        if (gcs.Count == 0)
        {
            md.Line("No GC events for this process (the trace needs the CLR GC keyword).");
            return;
        }

        var pause = gcs.Sum(g => g.PauseDurationMSec);
        md.Bullet($"GCs: {gcs.Count:N0}; total pause {MarkdownBuilder.Number(pause)} ms ({MarkdownBuilder.Percent(pause, data.DurationMs)} of {MarkdownBuilder.Number(data.DurationMs / 1000)} s); longest {MarkdownBuilder.Number(gcs.Max(g => g.PauseDurationMSec))} ms");
        md.Line();
        md.Table(["Generation", "GCs", "Pause ms", "Max pause ms"], gcs.GroupBy(g => g.Generation).OrderBy(g => g.Key)
            .Select(g => (IReadOnlyList<string>)[$"Gen {g.Key}", g.Count().ToString("N0"), MarkdownBuilder.Number(g.Sum(x => x.PauseDurationMSec)), MarkdownBuilder.Number(g.Max(x => x.PauseDurationMSec))]));

        md.Heading(3, "Longest pauses");
        md.Table(["At ms", "Gen", "Type", "Reason", "Pause ms", "Heap after MB"], gcs.OrderByDescending(g => g.PauseDurationMSec).Take(LongestPauses)
            .Select(g => (IReadOnlyList<string>)[MarkdownBuilder.Number(g.StartRelativeMSec, 0), g.Generation.ToString(), g.Type.ToString(), g.Reason.ToString(), MarkdownBuilder.Number(g.PauseDurationMSec), MarkdownBuilder.Number(g.HeapSizeAfterMB)]));

        md.Heading(3, "Heap size after GC over time");
        md.Line("A heap that keeps growing after Gen 2 GCs across repeated identical actions suggests a leak; confirm with `perfdigest heap` on two dumps.");
        md.Line();
        var step = Math.Max(1, gcs.Count / HeapTimelinePoints);
        md.Table(["At ms", "Gen", "Heap after MB", "Gen2 after MB", "LOH after MB"], gcs.Where((_, i) => i % step == 0 || i == gcs.Count - 1)
            .Select(g => (IReadOnlyList<string>)[MarkdownBuilder.Number(g.StartRelativeMSec, 0), g.Generation.ToString(), MarkdownBuilder.Number(g.HeapSizeAfterMB), MarkdownBuilder.Number(GenSize(g, Gens.Gen2)), MarkdownBuilder.Number(GenSize(g, Gens.GenLargeObj))]));
    }

    private static double GenSize(TraceGC gc, Gens gen)
    {
        try
        {
            return gc.GenSizeAfterMB(gen);
        }
        catch (Exception)
        {
            return 0;
        }
    }
}

using Microsoft.Diagnostics.Tracing.Etlx;
using PerfDigest.Cli;

namespace PerfDigest.Trace;

public static class ProcessPicker
{
    public static TraceProcess Pick(TraceLog log, string? requested)
    {
        var candidates = log.Processes.Where(p => p.ProcessID > 4).ToList();
        if (requested is not null)
        {
            var match = int.TryParse(requested, out var pid)
                ? candidates.Where(p => p.ProcessID == pid)
                : candidates.Where(p => p.Name.Equals(requested, StringComparison.OrdinalIgnoreCase)
                                        || p.Name.Equals(Path.GetFileNameWithoutExtension(requested), StringComparison.OrdinalIgnoreCase));
            return match.OrderByDescending(p => p.CPUMSec).FirstOrDefault()
                   ?? throw new UsageException($"Process '{requested}' not found. Present: {Describe(candidates)}");
        }

        var sampleCounts = new Dictionary<int, long>();
        foreach (var ev in log.Events)
        {
            if (TraceCollector.IsSample(ev))
            {
                sampleCounts[ev.ProcessID] = sampleCounts.GetValueOrDefault(ev.ProcessID) + 1;
            }
        }

        var best = candidates.OrderByDescending(p => sampleCounts.GetValueOrDefault(p.ProcessID)).ThenByDescending(p => p.CPUMSec).FirstOrDefault()
                   ?? throw new UsageException("The trace contains no user processes.");
        Progress.Info($"Analyzing {best.Name} (PID {best.ProcessID}); use --process to pick another. Present: {Describe(candidates)}");
        return best;
    }

    private static string Describe(IEnumerable<TraceProcess> processes) =>
        string.Join(", ", processes.OrderByDescending(p => p.CPUMSec).Take(8).Select(p => $"{p.Name} ({p.ProcessID})"));
}

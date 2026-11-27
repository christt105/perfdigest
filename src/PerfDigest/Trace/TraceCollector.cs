using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

namespace PerfDigest.Trace;

public static class TraceCollector
{
    private const string EventPipeSampleProvider = "Microsoft-DotNETCore-SampleProfiler";

    public static bool IsSample(TraceEvent ev) =>
        ev is SampledProfileTraceData || ev.ProviderName == EventPipeSampleProvider;

    public static TraceData Collect(TraceLog log, TraceProcess process, double? fromMs, double? toMs)
    {
        var data = new TraceData { ProcessId = process.ProcessID, ProcessName = process.Name };
        var interval = log.SampleProfileInterval.TotalMilliseconds;
        data.SampleIntervalMs = interval > 0 ? interval : 1;

        foreach (var ev in process.EventsInProcess)
        {
            var time = ev.TimeStampRelativeMSec;
            if (time < fromMs || time > toMs)
            {
                continue;
            }

            data.StartMs = Math.Min(data.StartMs, time);
            data.EndMs = Math.Max(data.EndMs, time);

            switch (ev)
            {
                case GCAllocationTickTraceData alloc:
                    Add(data.Allocations, new TypedStackKey(ev.CallStackIndex(), alloc.TypeName ?? "?"), alloc.AllocationAmount64, null);
                    break;
                case ExceptionTraceData exception:
                    Add(data.Exceptions, new TypedStackKey(ev.CallStackIndex(), exception.ExceptionType ?? "?"), 0, exception.ExceptionMessage);
                    break;
                default:
                    if (IsSample(ev))
                    {
                        if (ev.ProviderName == EventPipeSampleProvider)
                        {
                            data.SampleKind = "thread time: EventPipe samples every thread, waiting ones included";
                        }

                        var key = new SampleKey(ev.ThreadID, ev.CallStackIndex());
                        data.Samples[key] = data.Samples.GetValueOrDefault(key) + 1;
                        if (!data.ThreadNames.ContainsKey(ev.ThreadID))
                        {
                            data.ThreadNames[ev.ThreadID] = ev.Thread()?.ThreadInfo ?? "";
                        }
                    }

                    break;
            }
        }

        if (data.StartMs == double.MaxValue)
        {
            data.StartMs = 0;
        }

        return data;
    }

    private static void Add(Dictionary<TypedStackKey, Tally> target, TypedStackKey key, long amount, string? example)
    {
        if (!target.TryGetValue(key, out var tally))
        {
            tally = new Tally();
            target[key] = tally;
        }

        tally.Count++;
        tally.Amount += amount;
        tally.Example ??= string.IsNullOrWhiteSpace(example) ? null : example;
    }
}

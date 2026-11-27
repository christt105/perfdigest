namespace PerfDigest.Trace;

public sealed class TraceData
{
    public required int ProcessId { get; init; }

    public required string ProcessName { get; init; }

    public double StartMs { get; set; } = double.MaxValue;

    public double EndMs { get; set; }

    public double DurationMs => EndMs > StartMs ? EndMs - StartMs : 0;

    public double SampleIntervalMs { get; set; } = 1;

    public string SampleKind { get; set; } = "CPU";

    public Dictionary<SampleKey, long> Samples { get; } = [];

    public Dictionary<int, string> ThreadNames { get; } = [];

    public Dictionary<TypedStackKey, Tally> Allocations { get; } = [];

    public Dictionary<TypedStackKey, Tally> Exceptions { get; } = [];

    public long TotalSamples => Samples.Values.Sum();
}

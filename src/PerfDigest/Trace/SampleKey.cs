using Microsoft.Diagnostics.Tracing.Etlx;

namespace PerfDigest.Trace;

public readonly record struct SampleKey(int ThreadId, CallStackIndex Stack);

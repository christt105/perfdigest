using Microsoft.Diagnostics.Tracing.Etlx;

namespace PerfDigest.Trace;

public sealed record TraceInput(TraceLog Log, string Description, IReadOnlyList<string> Notes);

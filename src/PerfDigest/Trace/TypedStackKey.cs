using Microsoft.Diagnostics.Tracing.Etlx;

namespace PerfDigest.Trace;

public readonly record struct TypedStackKey(CallStackIndex Stack, string TypeName);

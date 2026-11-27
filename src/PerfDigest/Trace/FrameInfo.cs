namespace PerfDigest.Trace;

public sealed record FrameInfo(string Method, string Module, bool IsOwn)
{
    public string Display => IsOwn ? Markdown.NameShortener.Method(Method) : $"[{Module}] {Markdown.NameShortener.Method(Method)}";
}

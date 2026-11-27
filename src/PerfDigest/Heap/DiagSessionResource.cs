using System.IO.Compression;

namespace PerfDigest.Heap;

public sealed record DiagSessionResource(string Type, string Name, ZipArchiveEntry? Entry);

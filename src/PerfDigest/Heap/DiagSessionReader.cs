using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using PerfDigest.Cli;
using PerfDigest.Markdown;

namespace PerfDigest.Heap;

public static class DiagSessionReader
{
    public const string ResourceType = "MemoryProfiler.GCDump";
    private static readonly byte[] FastSerializationMagic = "!FastSerialization"u8.ToArray();
    private static readonly byte[] HeapDumpTypeName = "HeapDump"u8.ToArray();
    private static readonly string[] PathNames = ["path", "relativepath", "location", "uri", "filename", "file", "name"];

    public static IReadOnlyList<DiagSessionSnapshot> Extract(string diagSession, List<string> notes)
    {
        var targetDir = Path.Combine(WorkDirectory.For([diagSession]), "gcdump");
        using var zip = ZipFile.OpenRead(diagSession);
        var label = Path.GetFileName(diagSession);
        var entries = zip.Entries.Where(e => e.Length > 0).ToList();

        var declared = new List<(ZipArchiveEntry Entry, DateTime? Time, string TimeSource)>();
        var manifests = new List<string>();
        foreach (var manifest in entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var found = ManifestResources(manifest, entries);
            if (found.Count > 0)
            {
                manifests.Add(manifest.FullName);
                declared.AddRange(found.Where(f => declared.All(d => d.Entry != f.Entry)));
            }
        }

        if (declared.Count > 0)
        {
            notes.Add($"{label}: {declared.Count} `{ResourceType}` resource(s) declared in {string.Join(", ", manifests)}.");
        }
        else
        {
            declared = entries.Where(LooksLikeGcDump).Select(e => (e, (DateTime?)null, "")).ToList();
            notes.Add(declared.Count > 0
                ? $"{label}: no `{ResourceType}` resource found in its XML manifests; using the {declared.Count} entr(ies) that contain a .gcdump."
                : $"{label}: contains no managed heap snapshot. Entries: {string.Join("; ", entries.OrderByDescending(e => e.Length).Take(15).Select(e => $"{e.FullName} ({MarkdownBuilder.Bytes(e.Length)})"))}");
        }

        var result = new List<DiagSessionSnapshot>();
        foreach (var (entry, time, timeSource) in declared)
        {
            var target = Path.Combine(targetDir, entry.FullName.Replace('/', '_').Replace('\\', '_'));
            if (!File.Exists(target))
            {
                Directory.CreateDirectory(targetDir);
                Progress.Info($"Extracting {entry.FullName} ({MarkdownBuilder.Bytes(entry.Length)})");
                entry.ExtractToFile(target);
            }

            var resolved = time is null ? (entry.LastWriteTime.UtcDateTime, "archive entry time") : (time.Value, timeSource);
            result.Add(new DiagSessionSnapshot(entry.FullName, target, resolved.Item1, resolved.Item2));
        }

        return result.OrderBy(r => r.Time).ThenBy(r => r.EntryName, StringComparer.Ordinal).ToList();
    }

    private static List<(ZipArchiveEntry Entry, DateTime? Time, string TimeSource)> ManifestResources(ZipArchiveEntry manifest, List<ZipArchiveEntry> entries)
    {
        XDocument doc;
        try
        {
            using var stream = manifest.Open();
            doc = XDocument.Load(stream);
        }
        catch (Exception)
        {
            return [];
        }

        var result = new List<(ZipArchiveEntry, DateTime?, string)>();
        foreach (var element in doc.Descendants().Where(IsGcDumpResource))
        {
            var values = Values(element).ToList();
            var entry = values.Where(v => PathNames.Contains(v.Name.ToLowerInvariant()))
                .OrderBy(v => Array.IndexOf(PathNames, v.Name.ToLowerInvariant()))
                .Select(v => FindEntry(v.Value, entries))
                .FirstOrDefault(e => e is not null);
            if (entry is null)
            {
                continue;
            }

            var time = values.Where(v => IsTimeName(v.Name))
                .Select(v => (v.Name, Value: ParseTime(v.Value)))
                .FirstOrDefault(v => v.Value is not null);
            result.Add((entry, time.Value, time.Value is null ? "" : $"manifest attribute {time.Name}"));
        }

        return result;
    }

    private static bool IsGcDumpResource(XElement element) =>
        element.Attributes().Any(a => a.Value == ResourceType)
        || element.Elements().Any(c => !c.HasElements && c.Value.Trim() == ResourceType);

    private static IEnumerable<(string Name, string Value)> Values(XElement element)
    {
        foreach (var attribute in element.Attributes())
        {
            yield return (attribute.Name.LocalName, attribute.Value);
        }

        foreach (var child in element.Descendants().Where(d => !d.HasElements))
        {
            yield return (child.Name.LocalName, child.Value.Trim());
            foreach (var attribute in child.Attributes())
            {
                yield return (attribute.Name.LocalName, attribute.Value);
            }
        }
    }

    private static ZipArchiveEntry? FindEntry(string reference, List<ZipArchiveEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var normalized = Uri.UnescapeDataString(reference).Replace('\\', '/').TrimStart('/');
        return entries.FirstOrDefault(e => string.Equals(e.FullName, normalized, StringComparison.OrdinalIgnoreCase))
               ?? entries.FirstOrDefault(e => e.FullName.EndsWith("/" + normalized, StringComparison.OrdinalIgnoreCase))
               ?? entries.FirstOrDefault(e => normalized.Contains('/') && string.Equals(Path.GetFileName(e.FullName), Path.GetFileName(normalized), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTimeName(string name) =>
        name.Contains("time", StringComparison.OrdinalIgnoreCase) || name.Contains("date", StringComparison.OrdinalIgnoreCase);

    private static DateTime? ParseTime(string value)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0)
        {
            if (number > DateTime.MinValue.Ticks && number < DateTime.MaxValue.Ticks && number > new DateTime(1990, 1, 1).Ticks)
            {
                return new DateTime(number, DateTimeKind.Utc);
            }

            return number < 1_000_000_000_000_000 ? null : DateTime.FromFileTimeUtc(number);
        }

        return null;
    }

    private static bool LooksLikeGcDump(ZipArchiveEntry entry)
    {
        if (entry.FullName.EndsWith(".gcdump", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (entry.Length < 64)
        {
            return false;
        }

        using var stream = entry.Open();
        var header = new byte[128];
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        var span = header.AsSpan(0, read);
        return span.IndexOf(FastSerializationMagic) >= 0 && span.IndexOf(HeapDumpTypeName) >= 0;
    }
}

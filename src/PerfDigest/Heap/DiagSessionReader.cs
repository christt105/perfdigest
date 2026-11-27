using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PerfDigest.Cli;
using PerfDigest.Markdown;

namespace PerfDigest.Heap;

public static partial class DiagSessionReader
{
    private const string GcDumpType = "MemoryProfiler.GCDump";
    private const string ManifestType = "MemoryProfiler.Manifest";

    public static IReadOnlyList<DiagSessionSnapshot> Extract(string diagSession, List<string> notes)
    {
        var label = Path.GetFileName(diagSession);
        using var zip = ZipFile.OpenRead(diagSession);
        var resources = Resources(zip);
        var gcDumps = resources.Where(r => r.Type == GcDumpType && r.Entry is not null).ToList();

        var ordered = FromVsManifest(resources, gcDumps, label, notes);
        if (ordered is null)
        {
            var candidates = gcDumps.Count > 0
                ? gcDumps.Select(r => (r.Entry!, r.Name)).ToList()
                : zip.Entries.Where(e => e.Length > 0 && e.FullName.EndsWith(".gcdump", StringComparison.OrdinalIgnoreCase)).Select(e => (e, e.Name)).ToList();
            ordered = candidates
                .OrderBy(c => LeadingNumber(c.Name) ?? int.MaxValue)
                .ThenBy(c => c.Item1.FullName, StringComparer.Ordinal)
                .Select(c => (c.Item1, SnapshotName(c.Name), (DateTime?)null))
                .ToList();
            if (ordered.Count > 0)
            {
                notes.Add($"{label}: no Memory Usage snapshot list found; {ordered.Count} .gcdump file(s) ordered by the number their name starts with.");
            }
        }

        if (ordered.Count == 0)
        {
            notes.Add($"{label}: contains no managed heap snapshot. Entries: {string.Join("; ", zip.Entries.Where(e => e.Length > 0).OrderByDescending(e => e.Length).Take(15).Select(e => $"{e.FullName} ({MarkdownBuilder.Bytes(e.Length)})"))}");
            return [];
        }

        var targetDir = Path.Combine(WorkDirectory.For([diagSession]), "gcdump");
        var result = new List<DiagSessionSnapshot>();
        foreach (var (entry, name, time) in ordered)
        {
            var target = Path.Combine(targetDir, entry.FullName.Replace('/', '_').Replace('\\', '_'));
            if (!File.Exists(target) || new FileInfo(target).Length != entry.Length)
            {
                Directory.CreateDirectory(targetDir);
                Progress.Info($"Extracting {entry.FullName} ({MarkdownBuilder.Bytes(entry.Length)})");
                entry.ExtractToFile(target, overwrite: true);
            }

            result.Add(new DiagSessionSnapshot(entry.FullName, target, name, time));
        }

        return result;
    }

    private static List<(ZipArchiveEntry Entry, string Name, DateTime? Time)>? FromVsManifest(
        IReadOnlyList<DiagSessionResource> resources, IReadOnlyList<DiagSessionResource> gcDumps, string label, List<string> notes)
    {
        foreach (var manifest in resources.Where(r => r.Type == ManifestType && r.Entry is not null))
        {
            JsonDocument json;
            try
            {
                using var stream = manifest.Entry!.Open();
                json = JsonDocument.Parse(stream);
            }
            catch (JsonException)
            {
                continue;
            }

            using (json)
            {
                if (!json.RootElement.TryGetProperty("Snapshots", out var snapshots) || snapshots.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var result = new List<(ZipArchiveEntry, string, DateTime?, long)>();
                var missing = new List<string>();
                foreach (var snapshot in snapshots.EnumerateArray())
                {
                    var name = String(snapshot, "Name") ?? "?";
                    var order = snapshot.TryGetProperty("SnapshotTime", out var t) && t.TryGetInt64(out var ticks) ? ticks : long.MaxValue;
                    var heaps = snapshot.TryGetProperty("Heaps", out var h) && h.ValueKind == JsonValueKind.Array ? h.EnumerateArray().ToList() : [];
                    var files = heaps.Select(heap => (Heap: heap, File: String(heap, "GCDumpFileName"))).Where(x => x.File is not null).ToList();
                    if (files.Count == 0)
                    {
                        missing.Add(name);
                        continue;
                    }

                    foreach (var (heap, file) in files)
                    {
                        var resource = gcDumps.FirstOrDefault(r => string.Equals(r.Name, file, StringComparison.OrdinalIgnoreCase));
                        if (resource is null)
                        {
                            missing.Add(name);
                            continue;
                        }

                        result.Add((resource.Entry!, name, StartTime(heap), order));
                    }
                }

                if (result.Count == 0)
                {
                    continue;
                }

                notes.Add($"{label}: {result.Count} snapshot(s) in the order Visual Studio took them (its Memory Usage snapshot list, `{manifest.Entry!.FullName}`).");
                if (missing.Count > 0)
                {
                    notes.Add($"⚠ {label}: Visual Studio snapshot(s) {string.Join(", ", missing)} have no managed heap file in the session, so they are not in this comparison and the step across them covers two intervals.");
                }

                return result.OrderBy(r => r.Item4).Select(r => (r.Item1, r.Item2, r.Item3)).ToList();
            }
        }

        return null;
    }

    private static List<DiagSessionResource> Resources(ZipArchive zip)
    {
        var entries = zip.Entries.Where(e => e.Length > 0).ToList();
        var result = new List<DiagSessionResource>();
        foreach (var metadata in entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            XDocument doc;
            try
            {
                using var stream = metadata.Open();
                doc = XDocument.Load(stream);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var resource in doc.Descendants().Where(d => d.Name.LocalName == "Resource"))
            {
                var type = (string?)resource.Attribute("Type") ?? "";
                var name = (string?)resource.Attribute("Name") ?? "";
                var prefix = (string?)resource.Attribute("ResourcePackageUriPrefix") ?? "";
                var path = prefix.Length > 0 ? $"{prefix.Trim('/')}/{name}" : name;
                var entry = entries.FirstOrDefault(e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase))
                            ?? entries.FirstOrDefault(e => name.Length > 0 && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                result.Add(new DiagSessionResource(type, name, entry));
            }
        }

        return result;
    }

    private static string? String(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTime? StartTime(JsonElement heap)
    {
        if (!heap.TryGetProperty("StartTime", out var value) || !value.TryGetInt64(out var ticks))
        {
            return null;
        }

        var min = new DateTime(2000, 1, 1).Ticks;
        var max = new DateTime(2100, 1, 1).Ticks;
        return ticks > min && ticks < max ? new DateTime(ticks, DateTimeKind.Utc) : null;
    }

    private static string SnapshotName(string fileName) =>
        LeadingNumber(fileName)?.ToString(CultureInfo.InvariantCulture) ?? Path.GetFileNameWithoutExtension(fileName);

    private static int? LeadingNumber(string fileName)
    {
        var match = LeadingNumberPattern().Match(fileName);
        return match.Success && int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    [GeneratedRegex(@"^\d+")]
    private static partial Regex LeadingNumberPattern();
}

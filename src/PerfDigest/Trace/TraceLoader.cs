using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using PerfDigest.Cli;
using PerfDigest.Markdown;

namespace PerfDigest.Trace;

public static class TraceLoader
{
    public static TraceInput Load(IReadOnlyList<string> inputs)
    {
        var notes = new List<string>();
        var workDir = WorkDirectory(inputs);
        var files = new List<string>();
        foreach (var input in inputs)
        {
            if (input.EndsWith(".diagsession", StringComparison.OrdinalIgnoreCase))
            {
                files.AddRange(ExtractDiagSession(input, Path.Combine(workDir, "extracted"), notes));
            }
            else
            {
                files.Add(Path.GetFullPath(input));
            }
        }

        var etls = files.Where(f => f.EndsWith(".etl", StringComparison.OrdinalIgnoreCase)).ToList();
        var nettraces = files.Where(f => f.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase)).ToList();
        var etlx = Path.Combine(workDir, "trace.etlx");
        var options = new TraceLogOptions { ContinueOnError = true, ConversionLog = TextWriter.Null };

        if (!File.Exists(etlx))
        {
            if (etls.Count > 0)
            {
                if (!OperatingSystem.IsWindows())
                {
                    throw new UsageException("Reading .etl/.diagsession needs Windows (ETW). Use a .nettrace on other systems.");
                }

                Progress.Info($"Converting {etls.Count} ETL file(s) to ETLX (first run only, can take a while)...");
                using var source = new ETWTraceEventSource(etls, TraceEventSourceType.MergeAll);
                TraceLog.CreateFromEventTraceLogFile(source, etlx, options);
            }
            else if (nettraces.Count == 1)
            {
                Progress.Info("Converting .nettrace to ETLX...");
                TraceLog.CreateFromEventPipeDataFile(nettraces[0], etlx, options);
            }
            else
            {
                throw new UsageException(nettraces.Count > 1
                    ? "Only one .nettrace can be analyzed at a time."
                    : "No .etl or .nettrace data found in the input.");
            }
        }
        else
        {
            Progress.Info($"Reusing cached {etlx}");
        }

        var description = string.Join(", ", inputs.Select(Path.GetFileName));
        if (etls.Count > 0)
        {
            notes.Add($"Merged ETL files: {string.Join(", ", etls.Select(Path.GetFileName))}");
        }

        return new TraceInput(new TraceLog(etlx), description, notes);
    }

    private static IEnumerable<string> ExtractDiagSession(string path, string targetDir, List<string> notes)
    {
        using var zip = ZipFile.OpenRead(path);
        var listing = zip.Entries
            .Where(e => e.Length > 0)
            .OrderByDescending(e => e.Length)
            .Take(15)
            .Select(e => $"{e.FullName} ({MarkdownBuilder.Bytes(e.Length)})");
        notes.Add($"{Path.GetFileName(path)} contains: {string.Join("; ", listing)}");

        var extracted = new List<string>();
        foreach (var entry in zip.Entries.Where(e => e.Length > 0 && IsTraceFile(e.FullName)))
        {
            var target = Path.Combine(targetDir, Path.GetFileName(path) + "_" + entry.FullName.Replace('/', '_').Replace('\\', '_'));
            if (!File.Exists(target))
            {
                Directory.CreateDirectory(targetDir);
                Progress.Info($"Extracting {entry.FullName} ({MarkdownBuilder.Bytes(entry.Length)})");
                entry.ExtractToFile(target);
            }

            extracted.Add(target);
        }

        return extracted;
    }

    private static bool IsTraceFile(string name) =>
        name.EndsWith(".etl", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase);

    private static string WorkDirectory(IReadOnlyList<string> inputs)
    {
        var key = string.Join("|", inputs.Select(i =>
        {
            var info = new FileInfo(i);
            return $"{info.FullName}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        var dir = Path.Combine(Path.GetTempPath(), "perfdigest", hash);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

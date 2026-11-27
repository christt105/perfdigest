using System.Text.RegularExpressions;
using Graphs;
using PerfDigest.Cli;

namespace PerfDigest.Heap;

public static partial class GcDumpScanner
{
    private const int SamplesPerType = 3;

    public static (HeapSnapshot Snapshot, IRetentionGraph? Graph) Scan(SnapshotSource source, bool keepGraph)
    {
        Progress.Info($"Opening {source.Label}...");
        GCHeapDump dump;
        try
        {
            dump = new GCHeapDump(source.Path);
        }
        catch (Exception ex)
        {
            throw new UsageException($"{source.Label}: not a readable .gcdump ({ex.GetBaseException().Message}).");
        }

        var graph = dump.MemoryGraph;
        DateTime? collected = dump.TimeCollected.Ticks > 0 ? dump.TimeCollected.ToUniversalTime() : null;
        var parents = GcDumpRetentionGraph.Parents(graph);
        var multipliers = dump.CountMultipliersByType;
        var sampled = multipliers is { Length: > 0 } && multipliers.Any(m => m > 1.0001f);
        var snapshot = new HeapSnapshot
        {
            File = source.Label,
            Runtime = string.IsNullOrEmpty(dump.CreationTool) ? "gcdump" : dump.CreationTool,
            LiveOnly = true,
            Format = SnapshotFormat.GcDump,
            Time = source.Time ?? collected,
            CollectedTime = collected,
            Origin = source.Origin,
            HasDelegates = false,
            SamplingMultiplier = sampled ? dump.AverageCountMultiplier : null,
        };

        Progress.Info("Counting objects by type...");
        var node = graph.AllocNodeStorage();
        var type = graph.AllocTypeNodeStorage();
        var names = new string?[(int)graph.NodeTypeIndexLimit];
        var modules = new string?[names.Length];
        for (var index = (NodeIndex)0; index < graph.NodeIndexLimit; index++)
        {
            if (!graph.IsDefined(index))
            {
                continue;
            }

            graph.GetNode(index, node);
            var typeIndex = node.TypeIndex;
            if (names[(int)typeIndex] is null)
            {
                graph.GetType(typeIndex, type);
                names[(int)typeIndex] = TypeName(type.Name);
                modules[(int)typeIndex] = type.ModuleName;
            }

            var name = names[(int)typeIndex]!;
            if (GcDumpRetentionGraph.IsPseudoNode(name))
            {
                continue;
            }

            if (!snapshot.Types.TryGetValue(name, out var stat))
            {
                stat = new TypeStat();
                snapshot.Types[name] = stat;
                if (!string.IsNullOrEmpty(modules[(int)typeIndex]))
                {
                    snapshot.Modules[name] = modules[(int)typeIndex]!;
                }
            }

            var multiplier = sampled && (int)typeIndex < multipliers!.Length ? multipliers[(int)typeIndex] : 1f;
            stat.Count += (long)Math.Round(multiplier);
            stat.Size += (long)Math.Round(node.Size * (double)multiplier);
            if (stat.Samples.Count < SamplesPerType && parents[(int)index] != NodeIndex.Invalid)
            {
                stat.Samples.Add((ulong)index);
            }
        }

        return (snapshot, keepGraph ? new GcDumpRetentionGraph(graph, parents) : null);
    }

    public static string TypeName(string? raw) => raw is null ? "?" : SizeBucket().Replace(raw, "");

    [GeneratedRegex(@"\s*\(Bytes\s*>\s*[0-9.]+\s*[KMG]?\)$")]
    private static partial Regex SizeBucket();
}

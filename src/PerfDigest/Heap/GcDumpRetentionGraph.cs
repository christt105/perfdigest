using Graphs;

namespace PerfDigest.Heap;

public sealed class GcDumpRetentionGraph(MemoryGraph graph, NodeIndex[] parents) : IRetentionGraph
{
    private const int MaxHops = 14;

    public static bool IsPseudoNode(string typeName) => typeName.StartsWith('[');

    public static NodeIndex[] Parents(MemoryGraph graph)
    {
        var parents = new NodeIndex[(int)graph.NodeIndexLimit];
        Array.Fill(parents, NodeIndex.Invalid);
        if (graph.RootIndex == NodeIndex.Invalid)
        {
            return parents;
        }

        var node = graph.AllocNodeStorage();
        var queue = new Queue<NodeIndex>();
        parents[(int)graph.RootIndex] = graph.RootIndex;
        queue.Enqueue(graph.RootIndex);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            graph.GetNode(current, node);
            for (var child = node.GetFirstChildIndex(); child != NodeIndex.Invalid; child = node.GetNextChildIndex())
            {
                if (child >= 0 && (int)child < parents.Length && parents[(int)child] == NodeIndex.Invalid)
                {
                    parents[(int)child] = current;
                    queue.Enqueue(child);
                }
            }
        }

        return parents;
    }

    public string Describe(ulong sample)
    {
        var address = graph.GetAddress((NodeIndex)(int)sample);
        return address == 0 ? $"node {sample}" : $"instance 0x{address:x}";
    }

    public IReadOnlyList<string> Path(ulong sample)
    {
        var chain = new List<NodeIndex>();
        for (var current = (NodeIndex)(int)sample; current != NodeIndex.Invalid && chain.Count < 10_000; current = parents[(int)current])
        {
            chain.Add(current);
            if (current == graph.RootIndex)
            {
                break;
            }
        }

        chain.Reverse();
        var type = graph.AllocTypeNodeStorage();
        var node = graph.AllocNodeStorage();
        var names = chain.Select(index => GcDumpScanner.TypeName(graph.GetType(graph.GetNode(index, node).TypeIndex, type).Name)).ToList();
        var firstReal = names.FindIndex(n => !IsPseudoNode(n));
        var rootLabels = names.Take(firstReal < 0 ? names.Count : firstReal).Skip(1).Select(n => n.Trim('[', ']')).ToList();
        var lines = new List<string> { $"root: {(rootLabels.Count > 0 ? string.Join(" / ", rootLabels) : "?")}" };
        if (firstReal >= 0)
        {
            lines.AddRange(names.Skip(firstReal).Select((n, i) => i == 0 ? n : $"→ {n}"));
        }

        if (lines.Count > MaxHops + 1)
        {
            lines = [.. lines.Take(5), $"… {lines.Count - 13} more …", .. lines.TakeLast(8)];
        }

        return lines.Select((line, i) => new string(' ', Math.Min(i, 12) * 2) + line).ToList();
    }

    public void Dispose()
    {
    }
}

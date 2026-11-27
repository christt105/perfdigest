using Graphs;

namespace PerfDigest.Heap;

public sealed class GcDumpRetentionGraph(MemoryGraph graph, NodeIndex[] parents) : IRetentionGraph
{
    private const int MaxHops = 24;

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
        var type = graph.AllocTypeNodeStorage();
        parents[(int)graph.RootIndex] = graph.RootIndex;
        var categories = Children(graph, graph.RootIndex, node)
            .GroupBy(c => GcDumpRootKind.Priority(graph.GetType(graph.GetNode(c, node).TypeIndex, type).Name ?? ""))
            .OrderBy(g => g.Key);
        var queue = new Queue<NodeIndex>();
        foreach (var category in categories)
        {
            foreach (var child in category.Where(c => parents[(int)c] == NodeIndex.Invalid))
            {
                parents[(int)child] = graph.RootIndex;
                queue.Enqueue(child);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var child in Children(graph, current, node))
                {
                    if (parents[(int)child] == NodeIndex.Invalid)
                    {
                        parents[(int)child] = current;
                        queue.Enqueue(child);
                    }
                }
            }
        }

        return parents;
    }

    private static List<NodeIndex> Children(MemoryGraph graph, NodeIndex index, Node storage)
    {
        graph.GetNode(index, storage);
        var children = new List<NodeIndex>();
        for (var child = storage.GetFirstChildIndex(); child != NodeIndex.Invalid; child = storage.GetNextChildIndex())
        {
            if (child >= 0 && child < graph.NodeIndexLimit)
            {
                children.Add(child);
            }
        }

        return children;
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
        var rootLabels = names.Take(firstReal < 0 ? names.Count : firstReal).Skip(1).Select(GcDumpRootKind.Label).ToList();
        var lines = new List<string> { $"root: {(rootLabels.Count > 0 ? string.Join(" / ", rootLabels) : "?")}" };
        if (firstReal >= 0)
        {
            lines.AddRange(names.Skip(firstReal).Select((n, i) => i == 0 ? n : $"→ {n}"));
        }

        if (lines.Count > MaxHops + 1)
        {
            lines = [.. lines.Take(8), $"… {lines.Count - 20} more …", .. lines.TakeLast(12)];
        }

        return lines.Select((line, i) => new string(' ', Math.Min(i, 12) * 2) + line).ToList();
    }

    public void Dispose()
    {
    }
}

using Microsoft.Diagnostics.Runtime;

namespace PerfDigest.Heap;

public sealed class RetentionGraph(DataTarget target, ClrRuntime runtime, Dictionary<ulong, ulong> parents, Dictionary<ulong, ClrRootKind> rootKinds) : IRetentionGraph
{
    private const int MaxHops = 14;
    private Dictionary<ulong, string>? statics;

    public string Describe(ulong sample) => $"instance 0x{sample:x}";

    public IReadOnlyList<string> Path(ulong address)
    {
        var chain = new List<ulong>();
        for (var current = address; current != 0 && chain.Count < 10_000; current = parents.GetValueOrDefault(current))
        {
            chain.Add(current);
        }

        chain.Reverse();
        var labels = Statics();
        var start = chain.FindIndex(labels.ContainsKey);
        var lines = new List<string>();
        if (start >= 0)
        {
            lines.Add($"static {labels[chain[start]]}");
            chain = chain.Skip(start).ToList();
        }
        else
        {
            lines.Add($"root: {(chain.Count > 0 && rootKinds.TryGetValue(chain[0], out var kind) ? kind.ToString() : "?")}");
        }

        for (var i = 0; i < chain.Count; i++)
        {
            var field = i == 0 ? "" : FieldName(chain[i - 1], chain[i]);
            var type = runtime.Heap.GetObject(chain[i]).Type?.Name ?? "?";
            lines.Add($"{(field.Length > 0 ? $".{field} → " : "")}{type}");
        }

        if (lines.Count > MaxHops + 1)
        {
            lines = [.. lines.Take(5), $"… {lines.Count - 13} more …", .. lines.TakeLast(8)];
        }

        return lines.Select((line, i) => new string(' ', Math.Min(i, 12) * 2) + line).ToList();
    }

    public void Dispose()
    {
        runtime.Dispose();
        target.Dispose();
    }

    private string FieldName(ulong parent, ulong child)
    {
        var obj = runtime.Heap.GetObject(parent);
        if (obj.Type?.IsArray == true)
        {
            return "[]";
        }

        foreach (var reference in obj.EnumerateReferencesWithFields(carefully: false, considerDependantHandles: true))
        {
            if (reference.Object.Address != child)
            {
                continue;
            }

            if (reference.IsDependentHandle)
            {
                return "(dependent handle)";
            }

            var name = reference.Field?.Name ?? "?";
            var inner = reference.InnerField?.Field?.Name;
            return inner is null ? name : $"{name}.{inner}";
        }

        return "?";
    }

    private Dictionary<ulong, string> Statics()
    {
        if (statics is not null)
        {
            return statics;
        }

        statics = [];
        foreach (var module in runtime.EnumerateModules())
        {
            foreach (var (methodTable, _) in module.EnumerateTypeDefToMethodTableMap())
            {
                var type = runtime.Heap.GetTypeByMethodTable(methodTable);
                if (type is null)
                {
                    continue;
                }

                foreach (var field in type.StaticFields.Where(f => f.IsObjectReference))
                {
                    foreach (var domain in runtime.AppDomains)
                    {
                        try
                        {
                            var value = field.ReadObject(domain);
                            if (value.IsValid && !value.IsNull)
                            {
                                statics.TryAdd(value.Address, $"{type.Name}.{field.Name}");
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
        }

        return statics;
    }
}

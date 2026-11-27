using Microsoft.Diagnostics.Runtime;
using PerfDigest.Cli;

namespace PerfDigest.Heap;

public static class HeapScanner
{
    private const int SamplesPerType = 3;

    public static (HeapSnapshot Snapshot, RetentionGraph? Graph) Scan(string path, bool includeDead, bool keepGraph)
    {
        Progress.Info($"Opening {Path.GetFileName(path)}...");
        var target = DataTarget.LoadDump(path);
        var clr = target.ClrVersions.FirstOrDefault()
                  ?? throw new UsageException($"{path} has no .NET runtime loaded (is it a dump of a .NET process?).");
        var runtime = clr.CreateRuntime();
        var heap = runtime.Heap;
        if (!heap.CanWalkHeap)
        {
            throw new UsageException($"{path}: the heap cannot be walked (dump taken in the middle of a GC?). Take another one.");
        }

        Progress.Info("Marking live objects from GC roots...");
        var parents = new Dictionary<ulong, ulong>();
        var rootKinds = new Dictionary<ulong, ClrRootKind>();
        var queue = new Queue<ulong>();
        foreach (var root in heap.EnumerateRoots())
        {
            var address = root.Object.Address;
            if (address == 0 || parents.ContainsKey(address))
            {
                continue;
            }

            parents[address] = 0;
            rootKinds[address] = root.RootKind;
            queue.Enqueue(address);
        }

        while (queue.Count > 0)
        {
            var address = queue.Dequeue();
            var obj = heap.GetObject(address);
            if (!obj.IsValid)
            {
                continue;
            }

            foreach (var child in obj.EnumerateReferenceAddresses(carefully: false, considerDependantHandles: true))
            {
                if (child != 0 && parents.TryAdd(child, address))
                {
                    queue.Enqueue(child);
                }
            }
        }

        var snapshot = new HeapSnapshot { File = Path.GetFileName(path), Runtime = clr.Version.Major > 0 ? $"{clr.Flavor} {clr.Version}" : clr.Flavor.ToString(), LiveOnly = !includeDead };
        var objects = includeDead ? heap.EnumerateObjects() : parents.Keys.Select(heap.GetObject);
        var delegateTypes = new Dictionary<ulong, bool>();
        Progress.Info("Counting objects by type...");
        foreach (var obj in objects)
        {
            if (!obj.IsValid || obj.IsFree || obj.Type is null)
            {
                continue;
            }

            var name = obj.Type.Name ?? "?";
            if (!snapshot.Types.TryGetValue(name, out var stat))
            {
                stat = new TypeStat();
                snapshot.Types[name] = stat;
            }

            stat.Count++;
            stat.Size += (long)obj.Size;
            if (stat.Samples.Count < SamplesPerType && parents.ContainsKey(obj.Address))
            {
                stat.Samples.Add(obj.Address);
            }

            if (IsDelegate(obj.Type, delegateTypes))
            {
                CountDelegate(obj, runtime, snapshot);
            }
        }

        snapshot.FinalizableCount = heap.EnumerateFinalizableObjects().LongCount();

        if (keepGraph)
        {
            return (snapshot, new RetentionGraph(target, runtime, parents, rootKinds));
        }

        runtime.Dispose();
        target.Dispose();
        return (snapshot, null);
    }

    private static bool IsDelegate(ClrType type, Dictionary<ulong, bool> cache)
    {
        if (cache.TryGetValue(type.MethodTable, out var known))
        {
            return known;
        }

        var result = false;
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == "System.MulticastDelegate")
            {
                result = true;
                break;
            }
        }

        cache[type.MethodTable] = result;
        return result;
    }

    private static void CountDelegate(ClrObject obj, ClrRuntime runtime, HeapSnapshot snapshot)
    {
        try
        {
            var invocationList = obj.ReadObjectField("_invocationList");
            if (invocationList.IsValid && !invocationList.IsNull && invocationList.Type?.IsArray == true)
            {
                return;
            }

            var target = obj.ReadObjectField("_target");
            var targetName = target.IsNull || !target.IsValid ? "(static)" : target.Type?.Name ?? "?";
            if (targetName == obj.Type!.Name)
            {
                return;
            }

            var method = runtime.GetMethodByInstructionPointer(obj.ReadField<ulong>("_methodPtr"))
                         ?? runtime.GetMethodByInstructionPointer(obj.ReadField<ulong>("_methodPtrAux"));
            var key = method is null ? $"{targetName} via {obj.Type.Name}" : $"{targetName}.{method.Name} via {obj.Type.Name}";
            snapshot.DelegateTargets[key] = snapshot.DelegateTargets.GetValueOrDefault(key) + 1;
        }
        catch (Exception)
        {
        }
    }
}

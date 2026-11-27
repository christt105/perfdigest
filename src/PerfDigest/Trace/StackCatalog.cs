using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing.Etlx;
using PerfDigest.Cli;

namespace PerfDigest.Trace;

public sealed class StackCatalog(TraceLog log, CodeClassifier classifier)
{
    private readonly Dictionary<CallStackIndex, ResolvedStack> stacks = [];
    private readonly Dictionary<CodeAddressIndex, int> frameIds = [];
    private readonly Dictionary<(string, string), int> frameIdsByName = [];
    private readonly List<FrameInfo> frames = [];

    public IReadOnlyList<FrameInfo> Frames => frames;

    public FrameInfo Frame(int id) => frames[id];

    public ResolvedStack Resolve(CallStackIndex index)
    {
        if (index == CallStackIndex.Invalid)
        {
            return ResolvedStack.Empty;
        }

        if (stacks.TryGetValue(index, out var cached))
        {
            return cached;
        }

        var leafToRoot = new List<int>();
        for (var current = index; current != CallStackIndex.Invalid; current = log.CallStacks.Caller(current))
        {
            leafToRoot.Add(FrameId(log.CallStacks.CodeAddressIndex(current)));
        }

        leafToRoot.Reverse();
        var resolved = new ResolvedStack(leafToRoot.ToArray());
        stacks[index] = resolved;
        return resolved;
    }

    public void LoadSymbols(string symbolPath, IEnumerable<CallStackIndex> sampledStacks)
    {
        var modules = new HashSet<ModuleFileIndex>();
        foreach (var stack in sampledStacks)
        {
            for (var current = stack; current != CallStackIndex.Invalid; current = log.CallStacks.Caller(current))
            {
                var address = log.CallStacks.CodeAddressIndex(current);
                if (log.CodeAddresses.MethodIndex(address) == MethodIndex.Invalid)
                {
                    modules.Add(log.CodeAddresses.ModuleFileIndex(address));
                }
            }
        }

        modules.Remove(ModuleFileIndex.Invalid);
        using var reader = new SymbolReader(TextWriter.Null, symbolPath);
        reader.SecurityCheck = _ => true;
        foreach (var module in modules)
        {
            var file = log.ModuleFiles[module];
            Progress.Info($"Looking up symbols for {file.Name}");
            try
            {
                log.CodeAddresses.LookupSymbolsForModule(reader, file);
            }
            catch (Exception ex)
            {
                Progress.Info($"  failed: {ex.Message}");
            }
        }
    }

    private int FrameId(CodeAddressIndex address)
    {
        if (frameIds.TryGetValue(address, out var id))
        {
            return id;
        }

        var module = log.CodeAddresses.ModuleFile(address)?.Name ?? "?";
        var methodIndex = log.CodeAddresses.MethodIndex(address);
        var method = methodIndex == MethodIndex.Invalid ? "" : log.CodeAddresses.Methods.FullMethodName(methodIndex) ?? "";
        var key = (method.Length == 0 ? "?" : method, module);
        if (!frameIdsByName.TryGetValue(key, out id))
        {
            id = frames.Count;
            frames.Add(new FrameInfo(key.Item1, module, classifier.IsOwnMethod(method, module)));
            frameIdsByName[key] = id;
        }

        frameIds[address] = id;
        return id;
    }
}

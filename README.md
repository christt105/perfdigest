# perfdigest

Turns .NET profiling data into a compact Markdown report (usually 5-15 KB) that an LLM can read
without getting lost: time per method of your own code, hot call paths, GC pauses, allocations,
exceptions, and heap growth between memory dumps or Visual Studio/dotnet-gcdump heap snapshots with
the retention path that keeps leaked objects alive.

The raw formats (`.diagsession`, `.etl`, dumps) are huge and mostly noise; this tool does the
aggregation locally and only writes method and type names, so no source code or data leaves the
machine unless you paste the report somewhere.

## Download

Prebuilt single-file executables (no .NET install needed) are on the [releases page](../../releases):
`latest` is rebuilt on every push to `main`, `vX.Y.Z` releases come from tags.

## Build

Needs the .NET 8 SDK or newer.

```
dotnet build -c Release src/PerfDigest
dotnet src/PerfDigest/bin/Release/net8.0/PerfDigest.dll --help
```

Single Windows executable, no .NET install needed on the target machine:

```
dotnet publish src/PerfDigest -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## Time: `perfdigest trace`

```
perfdigest trace session.diagsession --focus MyCompany. --out time.md
perfdigest trace a.etl,b.etl --process MyApp --from 12000 --to 30000
perfdigest trace run.nettrace
```

Inputs:

- **Visual Studio `.diagsession`** (Windows only): it is a ZIP; every `.etl` inside is extracted and
  merged. Record with *CPU Usage*, and add *.NET Object Allocation Tracking* if you want allocations.
  The report lists what the archive contains.
- **`.etl`** from PerfView/WPR (Windows only), one or several comma-separated.
- **`.nettrace`** from `dotnet-trace` (.NET Core/5+, any OS). CPU + GC + allocations + exceptions:
  `dotnet-trace collect -p <pid> --providers "Microsoft-DotNETCore-SampleProfiler,Microsoft-Windows-DotNETRuntime:0x40008019:5"`.
  EventPipe samples every thread, waiting ones included, so the times are thread time, not CPU time.

The first run converts the trace to ETLX in `%TEMP%/perfdigest/<hash>`; later runs with the same
file reuse it. Delete that folder to reclaim the space.

Sections: time attributed to each method of your code (its self time plus framework/OS code it
calls directly), inclusive time, callee breakdown of the five heaviest methods, hot paths collapsed
to your frames, leaf self time, threads, GC pauses and heap-size timeline, allocations by type and
by allocating method, and first-chance exceptions by thrower.

`--focus` decides what "your code" is (namespace or module prefixes). Without it, everything outside
`System.*`, `Microsoft.*`, the runtime, common UI/vendor libraries and the OS counts as yours.
`--symbols` resolves native or precompiled (ReadyToRun/NGen) frames from a symbol path; JIT-compiled
managed code needs no symbols.

## Memory leaks: `perfdigest heap`

```
perfdigest heap before.dmp after1.dmp after2.dmp --focus MyCompany. --out leaks.md
perfdigest heap memory.diagsession --focus MyCompany.
perfdigest heap s1.gcdump s2.gcdump s3.gcdump
perfdigest heap one.dmp
```

Inputs (do not mix memory dumps with the other two, they name and count types differently):

- **Memory dumps** (`.dmp`, core dumps): read with ClrMD. Works for .NET Framework and .NET Core.
  Task Manager → right click the process → *Create memory dump file*, `procdump -ma <pid>`, or
  `dotnet-dump collect -p <pid>`. Analyze them on the same OS family that produced them (Windows
  dumps on Windows), since ClrMD loads the runtime's data access library from that platform.
- **Visual Studio `.diagsession`** from the *Memory Usage* tool: the snapshots are taken in the
  order of VS's own snapshot list (the `MemoryProfiler.Manifest` resource), with their names and
  capture times. The packaging time in `metadata.xml` is not the capture order and is not used.
  Snapshots VS lists without a managed heap file are reported as missing. Without that list, the
  `.gcdump` files are ordered by the number their name starts with. Several `.diagsession` files
  can be passed.
- **`.gcdump`** from `dotnet-gcdump collect -p <pid>`, PerfView, or a snapshot saved from VS. Any OS.

Recipe:

1. Start the app and do the suspicious action once (warms up caches and lazy singletons).
2. Take snapshot 1 (VS: *Take snapshot*). Repeat the action N times (open/close the same window, load
   the same document...).
3. Take snapshot 2. Repeat N times again, take snapshot 3.

Only objects reachable from GC roots are counted (for dumps, pass `--include-dead` to count garbage
too; `.gcdump` only stores live objects). The report shows your types that grew (✔ marks growth in
every step, the strongest leak signal), all types by size growth, every type that grew in every
step, a per-step table against the previous snapshot (the same Count Diff / Size Diff that VS shows
when you compare snapshot N with N-1), and for the top candidates the shortest path from a GC root
to one instance in the last snapshot.

Differences by format: dumps also give delegate targets that grew (event handlers that were never
removed) and field names on the retention path; `.gcdump` stores neither, so its paths list types
only and start at the root kind (`static var X.Field` from PerfView/dotnet-gcdump; strong, pinned,
stack... handles from VS). Paths prefer strong roots: an object reached only through a dependent
handle (`ConditionalWeakTable`, weak event patterns) is alive only while its owner is, so that path
is shown only when there is no other. Paths that repeat an earlier one are shortened to the part
that differs. PerfView splits
large arrays into size buckets such as `Byte[] (Bytes > 1K)`; they are merged back into one type,
as VS shows them. If the snapshot was sampled (very large heaps), counts and sizes are scaled by the
sampling multipliers and the overview says so.

## Using the report with an LLM

Paste the Markdown and ask a narrow question, e.g. "Given this profile of the *Open order* action,
which three methods should I look at first and what is the likely cause in each?" Then paste only
the source of those methods. For leaks: "Which of these retention paths is a bug, and where should
the reference be released?"

## License

MIT, see [LICENSE](LICENSE). `src/ThirdParty/PerfView.HeapDump` holds unmodified MIT-licensed
files from PerfView (via dotnet/diagnostics) that read the `.gcdump` format; see its README.

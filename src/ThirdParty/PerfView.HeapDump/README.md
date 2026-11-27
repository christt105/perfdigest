# PerfView heap dump reader (vendored)

`GCHeapDump.cs`, `Graph.cs`, `MemoryGraph.cs` and `DotNetHeapInfo.cs` are unmodified copies of the
files that dotnet/diagnostics keeps in `src/Tools/dotnet-gcdump/DotNetHeapDump`
(commit `86a05777478c2eab2df143f1866323c83ef252eb`), which in turn are copies of
[Microsoft/PerfView](https://github.com/microsoft/perfview). They read the `.gcdump` format written by
`dotnet-gcdump`, PerfView and Visual Studio's Memory Usage tool; no NuGet package ships them.

Treat them as read-only: replace them with a newer copy instead of editing them.

## License

The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

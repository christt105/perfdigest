using PerfDigest.Heap;
using PerfDigest.Trace;

namespace PerfDigest.Cli;

public static class CommandRunner
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage.Text);
            return args.Length == 0 ? 1 : 0;
        }

        var options = ArgReader.Parse(args.Skip(1).ToArray());
        try
        {
            var report = args[0] switch
            {
                "trace" => TraceCommand.Run(TraceOptions.From(options)),
                "heap" => HeapCommand.Run(HeapOptions.From(options)),
                _ => throw new UsageException($"Unknown command '{args[0]}'."),
            };
            Output.Write(report, options.Value("out"));
            return 0;
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage.Text);
            return 1;
        }
    }
}

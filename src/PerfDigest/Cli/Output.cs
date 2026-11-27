namespace PerfDigest.Cli;

public static class Output
{
    public static void Write(string report, string? path)
    {
        if (path is null)
        {
            Console.Out.Write(report);
            return;
        }

        File.WriteAllText(path, report);
        Console.Error.WriteLine($"Report written to {path} ({report.Length / 1024.0:F1} KB).");
    }
}

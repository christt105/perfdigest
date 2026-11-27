namespace PerfDigest.Cli;

public static class Progress
{
    public static void Info(string message) => Console.Error.WriteLine($"[perfdigest] {message}");
}

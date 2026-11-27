namespace PerfDigest.Cli;

public sealed class ParsedArgs(List<string> positional, Dictionary<string, string?> named)
{
    public IReadOnlyList<string> Positional => positional;

    public string? Value(string name) => named.TryGetValue(name, out var value) ? value : null;

    public bool Flag(string name) => named.ContainsKey(name);

    public int Int(string name, int fallback)
    {
        var raw = Value(name);
        if (raw is null)
        {
            return fallback;
        }

        return int.TryParse(raw, out var parsed) && parsed > 0
            ? parsed
            : throw new UsageException($"--{name} expects a positive integer, got '{raw}'.");
    }

    public double? Double(string name)
    {
        var raw = Value(name);
        if (raw is null)
        {
            return null;
        }

        return double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new UsageException($"--{name} expects a number, got '{raw}'.");
    }

    public IReadOnlyList<string> List(string name) =>
        (Value(name) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

using System.Globalization;
using System.Text;

namespace PerfDigest.Markdown;

public sealed class MarkdownBuilder
{
    private readonly StringBuilder text = new();

    public MarkdownBuilder Heading(int level, string title)
    {
        text.Append('\n').Append('#', level).Append(' ').AppendLine(title).AppendLine();
        return this;
    }

    public MarkdownBuilder Line(string line = "")
    {
        text.AppendLine(line);
        return this;
    }

    public MarkdownBuilder Bullet(string line) => Line($"- {line}");

    public MarkdownBuilder Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        text.Append("| ").AppendJoin(" | ", headers).AppendLine(" |");
        text.Append('|').AppendJoin("", headers.Select(h => IsNumericHeader(h) ? "---:|" : "---|")).AppendLine();
        var any = false;
        foreach (var row in rows)
        {
            any = true;
            text.Append("| ").AppendJoin(" | ", row.Select(Escape)).AppendLine(" |");
        }

        if (!any)
        {
            text.Append("| ").AppendJoin(" | ", headers.Select((_, i) => i == 0 ? "(none)" : "")).AppendLine(" |");
        }

        text.AppendLine();
        return this;
    }

    public MarkdownBuilder Code(IEnumerable<string> lines)
    {
        text.AppendLine("```");
        foreach (var line in lines)
        {
            text.AppendLine(line);
        }

        text.AppendLine("```").AppendLine();
        return this;
    }

    public override string ToString() => text.ToString();

    public static string Number(double value, int decimals = 1) =>
        value.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    public static string Percent(double part, double total) =>
        total <= 0 ? "0.0%" : (100.0 * part / total).ToString("F1", CultureInfo.InvariantCulture) + "%";

    public static string Bytes(double bytes)
    {
        var abs = Math.Abs(bytes);
        var (value, unit) = abs switch
        {
            >= 1L << 30 => (bytes / (1L << 30), "GB"),
            >= 1L << 20 => (bytes / (1L << 20), "MB"),
            >= 1L << 10 => (bytes / (1L << 10), "KB"),
            _ => (bytes, "B"),
        };
        return value.ToString(unit == "B" ? "F0" : "F1", CultureInfo.InvariantCulture) + " " + unit;
    }

    public static string Signed(long value) =>
        (value > 0 ? "+" : "") + value.ToString("N0", CultureInfo.InvariantCulture);

    public static string SignedBytes(long value) => (value > 0 ? "+" : "") + Bytes(value);

    private static readonly HashSet<string> NumericHeaders =
    [
        "ms", "%", "% of method", "Samples", "Count", "Counts", "Objects", "Finalizable", "Size", "Size now",
        "GCs", "Pause ms", "Max pause ms", "At ms", "Heap after MB", "Gen2 after MB", "LOH after MB",
    ];

    private static bool IsNumericHeader(string header) => NumericHeaders.Contains(header) || header.StartsWith('Δ');

    private static string Escape(string cell) => cell.Replace("|", "\\|").Replace("\n", " ");
}

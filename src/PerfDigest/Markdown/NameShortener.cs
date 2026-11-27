using System.Text.RegularExpressions;

namespace PerfDigest.Markdown;

public static partial class NameShortener
{
    private const int MaxLength = 140;

    public static string Method(string fullName)
    {
        var name = Parameters().Replace(fullName, "(…)");
        name = name.Replace("System.Collections.Generic.", "").Replace("System.Threading.Tasks.", "");
        return Truncate(name);
    }

    public static string Type(string fullName) => Truncate(fullName.Replace("System.Collections.Generic.", ""));

    private static string Truncate(string name) => name.Length <= MaxLength ? name : name[..(MaxLength - 1)] + "…";

    [GeneratedRegex(@"\((?>[^()]+|\((?<d>)|\)(?<-d>))+\)")]
    private static partial Regex Parameters();
}

using System.Security.Cryptography;
using System.Text;

namespace PerfDigest.Cli;

public static class WorkDirectory
{
    public static string For(IEnumerable<string> inputs)
    {
        var key = string.Join("|", inputs.Select(i =>
        {
            var info = new FileInfo(i);
            return $"{info.FullName}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        var dir = Path.Combine(Path.GetTempPath(), "perfdigest", hash);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

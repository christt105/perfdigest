namespace PerfDigest;

public sealed class CodeClassifier
{
    private static readonly string[] ExternalPrefixes =
    [
        "System.", "Microsoft.", "MS.", "Windows.", "WinRT.", "Internal.", "Interop", "<Module>",
        "Newtonsoft.", "Castle.", "Autofac.", "DevExpress.", "Telerik.", "Infragistics.", "Syncfusion.",
    ];

    private static readonly string[] ExternalModules =
    [
        "ntdll", "ntoskrnl", "kernel32", "kernelbase", "user32", "win32u", "gdi32", "gdi32full", "combase", "rpcrt4",
        "ucrtbase", "msvcrt", "msvcp_win", "clr", "clrjit", "coreclr", "mscoree", "mscoreei", "mscorlib",
        "mscorwks", "system", "netstandard", "presentationframework", "presentationcore", "windowsbase",
        "wpfgfx_v0400", "wpfgfx_cor3", "d3d9", "d3d11", "dxgi", "uxtheme", "dwmapi", "ole32", "oleaut32",
        "sechost", "advapi32", "shell32", "shlwapi", "comctl32", "uiautomationcore", "imm32", "msctf",
        "libcoreclr", "libclrjit", "libc", "libsystem.native", "libpthread",
    ];

    private readonly string[] focus;

    public CodeClassifier(IReadOnlyList<string> focusPrefixes)
    {
        focus = focusPrefixes.ToArray();
    }

    public bool HasFocus => focus.Length > 0;

    public bool IsOwnMethod(string methodName, string moduleName)
    {
        if (focus.Length > 0)
        {
            return focus.Any(f => methodName.StartsWith(f, StringComparison.OrdinalIgnoreCase)
                                  || moduleName.StartsWith(f, StringComparison.OrdinalIgnoreCase));
        }

        if (methodName.Length == 0 || IsExternalModule(moduleName))
        {
            return false;
        }

        return !ExternalPrefixes.Any(p => methodName.StartsWith(p, StringComparison.Ordinal));
    }

    public bool IsOwnType(string typeName)
    {
        if (focus.Length > 0)
        {
            return focus.Any(f => typeName.StartsWith(f, StringComparison.OrdinalIgnoreCase));
        }

        return !typeName.Contains('.') ? !IsPrimitiveLike(typeName)
            : !ExternalPrefixes.Any(p => typeName.StartsWith(p, StringComparison.Ordinal));
    }

    private static bool IsExternalModule(string moduleName)
    {
        var module = moduleName.ToLowerInvariant();
        return ExternalModules.Contains(module)
               || module.StartsWith("system.", StringComparison.Ordinal)
               || module.StartsWith("microsoft.", StringComparison.Ordinal)
               || module.StartsWith("presentation", StringComparison.Ordinal);
    }

    private static bool IsPrimitiveLike(string typeName) =>
        typeName is "Free" or "String" or "Object" || typeName.EndsWith("[]", StringComparison.Ordinal);
}

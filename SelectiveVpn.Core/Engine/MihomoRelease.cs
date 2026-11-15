using System.Runtime.InteropServices;

namespace SelectiveVpn.Engine;

public readonly record struct MihomoAsset(string FileName, string Sha256);

public static class MihomoRelease
{
    public const string Version = "1.19.33";

    public static string ExecutableFileName
        => OperatingSystem.IsWindows() ? "mihomo.exe" : "mihomo";

    public static bool TryGetAsset(Architecture architecture, out MihomoAsset asset)
        => TryGetAsset(CurrentOsKey(), architecture, out asset);

    public static bool TryGetAsset(string osKey, Architecture architecture, out MihomoAsset asset)
    {
        switch (osKey, architecture)
        {
            case ("windows", Architecture.X64):
                asset = new MihomoAsset(
                    "mihomo-windows-amd64-compatible-v1.19.33.zip",
                    "7aaf636a4f2172dcd8ec34ac4f52797b059195e5afd6409508c963e545f8864d");
                return true;
            case ("windows", Architecture.Arm64):
                asset = new MihomoAsset(
                    "mihomo-windows-arm64-v1.19.33.zip",
                    "a2a26869a14634c49f3fbcecc593b498ed8591fbdd7616b24475b3d60b148da3");
                return true;
            case ("linux", Architecture.X64):
                asset = new MihomoAsset(
                    "mihomo-linux-amd64-compatible-v1.19.33.gz",
                    "386508bfb1c72c43b6a0eda73708ecdce82546581b0ebcb16366c53757c622b6");
                return true;
            case ("linux", Architecture.Arm64):
                asset = new MihomoAsset(
                    "mihomo-linux-arm64-v1.19.33.gz",
                    "ba408b317080ee265c46cb923a50018c1b82cbd708551cdd46a1e3e307396187");
                return true;
            case ("darwin", Architecture.X64):
                asset = new MihomoAsset(
                    "mihomo-darwin-amd64-compatible-v1.19.33.gz",
                    "eefe967b74312f70b64efa0dfa54929d00c7af8e94416e1b12df09863a40f35b");
                return true;
            case ("darwin", Architecture.Arm64):
                asset = new MihomoAsset(
                    "mihomo-darwin-arm64-v1.19.33.gz",
                    "060b64b0f2c9a310831db0fe4adb2878de67bfa606c311813c1f13966aa504b6");
                return true;
            default:
                asset = default;
                return false;
        }
    }

    public static string DownloadUrl(MihomoAsset asset)
        => $"https://github.com/MetaCubeX/mihomo/releases/download/v{Version}/{asset.FileName}";

    public static bool IsGzipAsset(MihomoAsset asset)
        => asset.FileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
           && !asset.FileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);

    private static string CurrentOsKey()
    {
        if (OperatingSystem.IsWindows())
            return "windows";
        if (OperatingSystem.IsLinux())
            return "linux";
        if (OperatingSystem.IsMacOS())
            return "darwin";
        return "unknown";
    }
}

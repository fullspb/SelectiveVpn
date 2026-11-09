using System.Runtime.InteropServices;

namespace SelectiveVpn.Engine;

public readonly record struct MihomoAsset(string FileName, string Sha256);

public static class MihomoRelease
{
    public const string Version = "1.19.33";

    public static bool TryGetAsset(Architecture architecture, out MihomoAsset asset)
    {
        switch (architecture)
        {
            case Architecture.X64:
                asset = new MihomoAsset(
                    "mihomo-windows-amd64-compatible-v1.19.33.zip",
                    "7aaf636a4f2172dcd8ec34ac4f52797b059195e5afd6409508c963e545f8864d");
                return true;
            case Architecture.Arm64:
                asset = new MihomoAsset(
                    "mihomo-windows-arm64-v1.19.33.zip",
                    "a2a26869a14634c49f3fbcecc593b498ed8591fbdd7616b24475b3d60b148da3");
                return true;
            default:
                asset = default;
                return false;
        }
    }

    public static string DownloadUrl(MihomoAsset asset)
        => $"https://github.com/MetaCubeX/mihomo/releases/download/v{Version}/{asset.FileName}";
}

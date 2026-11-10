using System.Security.Cryptography;
using System.Text;

namespace SelectiveVpn.Engine;

public static class DeviceIdentity
{
    private static readonly object Gate = new();

    public static string GetHardwareId(string? rootDirectory = null)
    {
        var path = Path.Combine(
            rootDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SelectiveVpn"),
            "device-id.txt");

        lock (Gate)
        {
            try
            {
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path).Trim();
                    if (IsValid(existing))
                        return existing;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var created = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                File.WriteAllText(path, created, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return created;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName)))
                    .ToLowerInvariant()[..32];
            }
        }
    }

    public static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length is >= 8 and <= 64
           && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '=');
}

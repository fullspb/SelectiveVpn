using System.Runtime.InteropServices;
using SelectiveVpn.Engine;
using SelectiveVpn.Models;

namespace SelectiveVpn.Tests;

public class HostPlatformTests
{
    [Fact]
    public void Device_os_name_matches_current_operating_system()
    {
        if (OperatingSystem.IsLinux())
            Assert.Equal("Linux", HostPlatform.DeviceOsName);
        else if (OperatingSystem.IsWindows())
            Assert.Equal("Windows", HostPlatform.DeviceOsName);
        else if (OperatingSystem.IsMacOS())
            Assert.Equal("macOS", HostPlatform.DeviceOsName);
    }

    [Fact]
    public void Mihomo_release_exposes_current_os_asset_and_executable_name()
    {
        Assert.True(MihomoRelease.TryGetAsset(RuntimeInformation.ProcessArchitecture, out var asset));
        Assert.False(string.IsNullOrWhiteSpace(asset.FileName));
        Assert.Equal(64, asset.Sha256.Length);

        if (OperatingSystem.IsWindows())
        {
            Assert.StartsWith("mihomo-windows-", asset.FileName, StringComparison.Ordinal);
            Assert.EndsWith(".zip", asset.FileName, StringComparison.Ordinal);
            Assert.Equal("mihomo.exe", MihomoRelease.ExecutableFileName);
            Assert.False(MihomoRelease.IsGzipAsset(asset));
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.StartsWith("mihomo-linux-", asset.FileName, StringComparison.Ordinal);
            Assert.EndsWith(".gz", asset.FileName, StringComparison.Ordinal);
            Assert.Equal("mihomo", MihomoRelease.ExecutableFileName);
            Assert.True(MihomoRelease.IsGzipAsset(asset));
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.StartsWith("mihomo-darwin-", asset.FileName, StringComparison.Ordinal);
            Assert.EndsWith(".gz", asset.FileName, StringComparison.Ordinal);
            Assert.Equal("mihomo", MihomoRelease.ExecutableFileName);
            Assert.True(MihomoRelease.IsGzipAsset(asset));
        }
    }

    [Theory]
    [InlineData("windows", Architecture.X64, "mihomo-windows-amd64-compatible-v1.19.33.zip")]
    [InlineData("windows", Architecture.Arm64, "mihomo-windows-arm64-v1.19.33.zip")]
    [InlineData("linux", Architecture.X64, "mihomo-linux-amd64-compatible-v1.19.33.gz")]
    [InlineData("linux", Architecture.Arm64, "mihomo-linux-arm64-v1.19.33.gz")]
    [InlineData("darwin", Architecture.X64, "mihomo-darwin-amd64-compatible-v1.19.33.gz")]
    [InlineData("darwin", Architecture.Arm64, "mihomo-darwin-arm64-v1.19.33.gz")]
    public void Mihomo_release_catalog_covers_windows_linux_and_macos(string os, Architecture arch, string fileName)
    {
        Assert.True(MihomoRelease.TryGetAsset(os, arch, out var asset));
        Assert.Equal(fileName, asset.FileName);
        Assert.Equal(64, asset.Sha256.Length);
        Assert.Contains(fileName, MihomoRelease.DownloadUrl(asset), StringComparison.Ordinal);
    }

    [Fact]
    public void Process_tree_expander_default_is_supported_on_windows_and_linux()
    {
        var expander = ProcessTreeExpander.CreateDefault();
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            Assert.True(expander.IsSupported);
        else
            Assert.False(expander.IsSupported);

        Assert.Empty(expander.Expand([]));
        Assert.False(NullProcessTreeExpander.Instance.IsSupported);
        Assert.Empty(NullProcessTreeExpander.Instance.Expand([new RouteTarget("/bin/true")]));
    }

    [Fact]
    public void Subscription_yaml_uses_actual_device_os_header()
    {
        var yaml = MihomoProfileWriter.BuildSubscription(
            new SubscriptionProfile { Url = "https://example.invalid/subscription" },
            [],
            17990,
            "synthetic-controller-secret",
            "provider.yaml",
            "synthetic-hardware-id");

        Assert.Contains("x-device-os:", yaml);
        Assert.Contains($"- \"{HostPlatform.DeviceOsName}\"", yaml);
        if (!OperatingSystem.IsWindows())
            Assert.DoesNotContain("- \"Windows\"", yaml);
    }
}

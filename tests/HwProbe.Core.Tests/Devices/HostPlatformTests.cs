using System.Runtime.Versioning;
using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Devices;

/// <summary>The real host platform's file checks.</summary>
[Trait("Category", "Unit")]
[Trait("Category", "Platform")]
public sealed class HostPlatformTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("hwprobe-platform-").FullName;

    /// <summary>A file with no permissions is denied, a normal file isn't, and a missing one isn't reported as denied.</summary>
    [Fact(Skip = "Requires Linux or macOS: Unix file modes.", SkipUnless = nameof(TestEnvironment.IsPosix), SkipType = typeof(TestEnvironment))]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public void DeniedOnlyForPermissionFailures()
    {
        var locked = Path.Combine(_directory, "locked");
        var open = Path.Combine(_directory, "open");
        File.WriteAllText(locked, string.Empty);
        File.WriteAllText(open, string.Empty);
        File.SetUnixFileMode(locked, UnixFileMode.None);
        var platform = new HostPlatform();

        Assert.Equal(Environment.UserName != "root", platform.IsAccessDenied(locked));
        Assert.False(platform.IsAccessDenied(open));
        Assert.False(platform.IsAccessDenied(Path.Combine(_directory, "missing")));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
        {
            foreach (var file in Directory.EnumerateFiles(_directory))
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        Directory.Delete(_directory, recursive: true);
    }
}

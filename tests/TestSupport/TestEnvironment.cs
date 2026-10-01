using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;

namespace Jellyfin.Plugin.HwProbe.TestSupport;

/// <summary>Skip conditions for platform- and hardware-bound tests, for use with <c>SkipUnless</c>.</summary>
/// <example>
/// <c>[Fact(Skip = "Requires macOS.", SkipUnless = nameof(TestEnvironment.IsMacOS), SkipType = typeof(TestEnvironment))]</c>.
/// </example>
internal static class TestEnvironment
{
    /// <summary>Gets a value indicating whether the tests run on Linux.</summary>
    public static bool IsLinux => OperatingSystem.IsLinux();

    /// <summary>Gets a value indicating whether the tests run on macOS.</summary>
    public static bool IsMacOS => OperatingSystem.IsMacOS();

    /// <summary>Gets a value indicating whether the tests run on Linux or macOS, which have a POSIX shell.</summary>
    public static bool IsPosix => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    /// <summary>Gets a value indicating whether the tests run on Windows.</summary>
    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>Gets a value indicating whether RealFfmpeg and Hardware tests were opted into with <c>HWPROBE_HW_TESTS=1</c>.</summary>
    public static bool HardwareTestsEnabled => Environment.GetEnvironmentVariable("HWPROBE_HW_TESTS") == "1";

    /// <summary>Gets the real ffmpeg for RealFfmpeg tests: <c>HWPROBE_TEST_FFMPEG</c>, else normal discovery.</summary>
    public static string? RealFfmpeg { get; } = LocateRealFfmpeg();

    /// <summary>Gets a value indicating whether RealFfmpeg tests are enabled and an ffmpeg was found.</summary>
    public static bool RealFfmpegAvailable => HardwareTestsEnabled && RealFfmpeg is not null;

    /// <summary>Gets a value indicating whether RealFfmpeg tests can run on macOS.</summary>
    public static bool RealFfmpegOnMacOS => RealFfmpegAvailable && IsMacOS;

    /// <summary>Resolves the ffmpeg for RealFfmpeg tests.</summary>
    /// <returns>Its path, or null when none is found.</returns>
    private static string? LocateRealFfmpeg()
    {
        try
        {
            return new FfmpegLocator().Locate(Environment.GetEnvironmentVariable("HWPROBE_TEST_FFMPEG"))?.Path;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}

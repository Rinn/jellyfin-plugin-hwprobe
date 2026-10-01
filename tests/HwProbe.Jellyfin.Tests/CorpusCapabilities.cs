using Jellyfin.Plugin.HwProbe.Core.Ffmpeg;
using Jellyfin.Plugin.HwProbe.Core.Pipeline;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.TestSupport;

namespace Jellyfin.Plugin.HwProbe.Jellyfin.Tests;

/// <summary>Builds stub capabilities from a recorded jellyfin-ffmpeg build dump.</summary>
internal static class CorpusCapabilities
{
    /// <summary>Runs build enumeration over a recorded build and maps it as the CLI does.</summary>
    /// <param name="build">Directory under <c>Corpus/ffmpeg</c>.</param>
    /// <param name="driver">The VAAPI driver the device open would have found.</param>
    /// <returns>The capabilities EncodingHelper sees.</returns>
    public static async Task<ProbeCapabilities> LoadAsync(string build, VaapiDriver driver = VaapiDriver.IntelIhd)
    {
        var caps = await new FfmpegCapabilityProbe(ScriptedFfmpegRunner.FromCorpus(build), TimeSpan.FromSeconds(5))
            .ProbeAsync("ffmpeg", CancellationToken.None);
        return ArgumentSourceFactory.ToProbeCapabilities(caps, new DeviceTraits(driver));
    }
}

using Jellyfin.Plugin.HwProbe.Probing;
using MediaBrowser.Model.Session;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.PluginTests;

/// <summary>Which sessions <see cref="ProbeService"/> counts as busy.</summary>
[Trait("Category", "Unit")]
public sealed class ProbeServiceBusyTests
{
    /// <summary>Direct play and a remux don't count as busy; re-encoding video or audio does.</summary>
    [Fact]
    public void RemuxIsNotBusy()
    {
        Assert.False(ProbeService.IsTranscoding(null));
        Assert.False(ProbeService.IsTranscoding(new TranscodingInfo { IsVideoDirect = true, IsAudioDirect = true }));
        Assert.True(ProbeService.IsTranscoding(new TranscodingInfo { IsVideoDirect = true, IsAudioDirect = false }));
        Assert.True(ProbeService.IsTranscoding(new TranscodingInfo { IsVideoDirect = false, IsAudioDirect = true }));
    }
}

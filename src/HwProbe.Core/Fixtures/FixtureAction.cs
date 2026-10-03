namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>What the fixture builder is doing to a clip.</summary>
public enum FixtureAction
{
    /// <summary>Downloading it, or the piece it's made from.</summary>
    Downloading,

    /// <summary>Generating it with ffmpeg.</summary>
    Generating,
}

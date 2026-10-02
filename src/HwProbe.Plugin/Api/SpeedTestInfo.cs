namespace Jellyfin.Plugin.HwProbe.Api;

/// <summary>One speed test, as the page lists it.</summary>
/// <param name="Key">The test key.</param>
/// <param name="Label">What it measures.</param>
/// <param name="DecodeOnly">Whether it only decodes, so it reports fps but no stream count.</param>
/// <param name="Interlaced">Whether its source is interlaced, so the deinterlace comparison applies.</param>
/// <param name="Default">Whether it's chosen when the page first loads.</param>
/// <param name="FrameRate">The source frame rate, so fps can be shown as a multiple of real time.</param>
public sealed record SpeedTestInfo(string Key, string Label, bool DecodeOnly, bool Interlaced, bool Default, float FrameRate);

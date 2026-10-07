namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>A file's video stream.</summary>
/// <param name="Index">The stream index in the file.</param>
/// <param name="Codec">The codec as Jellyfin names it, e.g. <c>hevc</c>.</param>
/// <param name="BitDepth">The bit depth.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
/// <param name="FrameRate">The frame rate.</param>
public sealed record SpeedFileVideo(int Index, string Codec, int BitDepth, int Width, int Height, float FrameRate)
{
    /// <summary>Gets the profile, e.g. <c>Main 10</c>, or null.</summary>
    public string? Profile { get; init; }

    /// <summary>Gets the pixel format, e.g. <c>yuv420p10le</c>, or null.</summary>
    public string? PixelFormat { get; init; }

    /// <summary>Gets a value indicating whether the stream is interlaced.</summary>
    public bool Interlaced { get; init; }

    /// <summary>Gets the colour transfer, e.g. <c>smpte2084</c>, or null.</summary>
    public string? ColorTransfer { get; init; }

    /// <summary>Gets the colour primaries, or null.</summary>
    public string? ColorPrimaries { get; init; }

    /// <summary>Gets the colour space, or null.</summary>
    public string? ColorSpace { get; init; }

    /// <summary>Gets the display aspect ratio as Jellyfin reports it, e.g. <c>16:9</c>, or null.</summary>
    public string? AspectRatio { get; init; }

    /// <summary>Gets a value indicating whether the stream is HDR10 or HLG, which Jellyfin tone-maps.</summary>
    public bool IsHdr => ColorTransfer is "smpte2084" or "arib-std-b67";
}

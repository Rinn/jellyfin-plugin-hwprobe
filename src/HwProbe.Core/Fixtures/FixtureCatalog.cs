namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>The fixture clips from PLAN.md: 640x360, 25 frames of testsrc2.</summary>
public static class FixtureCatalog
{
    private const string Source = "-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=640x360:rate=25 -frames:v 25";

    /// <summary>Gets the 8-bit H.264 clip, the smoke-probe source.</summary>
    public static FixtureSpec H264 { get; } = new("h264_8bit.mp4", "h264", 8, false, "libx264", $"{Source} -c:v libx264 -pix_fmt yuv420p", null);

    /// <summary>Gets the interlaced (top field first) 8-bit H.264 clip, for deinterlacing.</summary>
    public static FixtureSpec H264Interlaced { get; } = new(
        "h264_interlaced.mp4",
        "h264",
        8,
        false,
        "libx264",
        $"{Source} -vf setfield=tff -c:v libx264 -flags +ildct+ilme -x264-params tff=1 -pix_fmt yuv420p",
        null)
    {
        Interlaced = true,
    };

    /// <summary>Gets a one-line text subtitle (ASS), burned in when Jellyfin's "Allow subtitle extraction on the fly" is off.</summary>
    /// <remarks>Made from an inline SRT through ffmpeg's data: protocol, so nothing is read from disk.</remarks>
    public static FixtureSpec SubtitlesAss { get; } = new(
        "subtitles.ass",
        "ass",
        8,
        false,
        "ass",
        $"-hide_banner -loglevel error -y -f srt -i \"data:text/plain;base64,{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("1\n00:00:00,000 --> 00:00:01,000\nhwprobe subtitle burn-in\n"))}\" -c:s ass",
        null);

    /// <summary>Gets the 8-bit HEVC clip.</summary>
    public static FixtureSpec Hevc { get; } = new("hevc_8bit.mp4", "hevc", 8, false, "libx265", $"{Source} -c:v libx265 -pix_fmt yuv420p", null);

    /// <summary>Gets the 10-bit HEVC clip.</summary>
    public static FixtureSpec Hevc10 { get; } = new("hevc_10bit.mp4", "hevc", 10, false, "libx265", $"{Source} -c:v libx265 -pix_fmt yuv420p10le", null);

    /// <summary>Gets the HDR10 clip for the tone-map probes.</summary>
    public static FixtureSpec Hdr10 { get; } = new(
        "hdr10.mp4",
        "hevc",
        10,
        true,
        "libx265",
        $"{Source} -c:v libx265 -pix_fmt yuv420p10le -color_primaries {ColorMetadata.Hdr10.Primaries} -color_trc {ColorMetadata.Hdr10.Transfer} -colorspace {ColorMetadata.Hdr10.Space}",
        null);

    /// <summary>Gets the 8-bit VP9 clip.</summary>
    public static FixtureSpec Vp9 { get; } = new("vp9_8bit.webm", "vp9", 8, false, "libvpx-vp9", $"{Source} -c:v libvpx-vp9 -pix_fmt yuv420p", null);

    /// <summary>Gets the 10-bit VP9 clip (profile 2).</summary>
    public static FixtureSpec Vp910 { get; } = new("vp9_10bit.webm", "vp9", 10, false, "libvpx-vp9", $"{Source} -c:v libvpx-vp9 -pix_fmt yuv420p10le", null);

    /// <summary>Gets the VP8 clip.</summary>
    public static FixtureSpec Vp8 { get; } = new("vp8.webm", "vp8", 8, false, "libvpx", $"{Source} -c:v libvpx -pix_fmt yuv420p", null);

    /// <summary>Gets the 10-bit HEVC range-extension clip (4:2:2), Jellyfin's "HEVC RExt 8/10bit".</summary>
    public static FixtureSpec HevcRext10 { get; } = new("hevc_rext_10bit.mp4", "hevc", 10, false, "libx265", $"{Source} -c:v libx265 -pix_fmt yuv422p10le", null)
    {
        PixelFormat = "yuv422p10le",
        Profile = "Rext",
    };

    /// <summary>Gets the 12-bit HEVC range-extension clip (4:4:4), Jellyfin's "HEVC RExt 12bit".</summary>
    public static FixtureSpec HevcRext12 { get; } = new("hevc_rext_12bit.mp4", "hevc", 12, false, "libx265", $"{Source} -c:v libx265 -pix_fmt yuv444p12le", null)
    {
        PixelFormat = "yuv444p12le",
        Profile = "Rext",
    };

    /// <summary>Gets the 10-bit AV1 clip.</summary>
    public static FixtureSpec Av110 { get; } = new("av1_10bit.mp4", "av1", 10, false, "libsvtav1", $"{Source} -c:v libsvtav1 -pix_fmt yuv420p10le", null);

    /// <summary>Gets the 8-bit AV1 clip.</summary>
    public static FixtureSpec Av1 { get; } = new("av1_8bit.mp4", "av1", 8, false, "libsvtav1", $"{Source} -c:v libsvtav1 -pix_fmt yuv420p", null);

    /// <summary>Gets the MPEG-2 clip.</summary>
    public static FixtureSpec Mpeg2 { get; } = new("mpeg2.mpg", "mpeg2video", 8, false, "mpeg2video", $"{Source} -c:v mpeg2video", null);

    /// <summary>Gets the VC-1 Advanced Profile clip (320x240, 30 frames, progressive).</summary>
    /// <remarks>
    /// No free VC-1 encoder exists, so this is downloaded from FFmpeg's public FATE sample suite rather than generated.
    /// It's a conformance stream with no clear redistribution terms, so it's fetched and pinned by hash, not checked in.
    /// </remarks>
    public static FixtureSpec Vc1 { get; } = new("vc1_SA00050.vc1", "vc1", 8, false, null, string.Empty, null)
    {
        DownloadUrl = new Uri("https://fate-suite.ffmpeg.org/vc1/SA00050.vc1"),
        Sha256 = "29cf8bebc87be73a1b7a6169aff4c1c36d8891a20c200da7676560fefe3e461c",
    };

    /// <summary>Gets every fixture, including ones that can never be generated.</summary>
    public static IReadOnlyList<FixtureSpec> All { get; } = [H264, H264Interlaced, SubtitlesAss, Hevc, Hevc10, HevcRext10, HevcRext12, Hdr10, Vp8, Vp9, Vp910, Av1, Av110, Mpeg2, Vc1];
}

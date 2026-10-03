using System.Text.Json.Serialization;
using Jellyfin.Plugin.HwProbe.Core.Model;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>One measured backend, test and variant.</summary>
/// <param name="Type">The backend; <see cref="HwType.none"/> is software.</param>
/// <param name="Device">Render node or adapter, or empty.</param>
/// <param name="Test">The <see cref="SpeedTest.Key"/>.</param>
/// <param name="Variant">The comparison, e.g. <c>VBR audio on</c>, or empty for the base settings.</param>
/// <param name="Fps">Frames per second of one transcode, or null.</param>
/// <param name="Streams">Transcodes that keep real time at once, or null for a decode test or when not measured.</param>
/// <param name="Capped">Whether <see cref="Streams"/> is a lower bound.</param>
/// <param name="Note">Why something is missing or what Jellyfin does instead, or null.</param>
public sealed record SpeedResult(HwType Type, string Device, string Test, string Variant, double? Fps, int? Streams, bool Capped, string? Note)
{
    /// <summary>Gets the test's label, so a report reads without the catalog (a file's tests aren't in it).</summary>
    public string? Label { get; init; }

    /// <summary>Gets the video's name, e.g. <c>Test video</c>.</summary>
    public string? Video { get; init; }

    /// <summary>Gets the output's label, e.g. <c>720p H.264, 4 Mbps</c>.</summary>
    public string? Output { get; init; }

    /// <summary>Gets what the video is, e.g. <c>1080p H.264, 24 fps, 5.1 AAC</c>.</summary>
    public string? Input { get; init; }

    /// <summary>Gets the source frame rate, so fps can be shown as a multiple of real time.</summary>
    public float? FrameRate { get; init; }

    /// <summary>Gets the credit a sample's licence requires, or null.</summary>
    public string? Credit { get; init; }

    /// <summary>Gets the sample's licence, or null.</summary>
    public Uri? LicenseUrl { get; init; }

    /// <summary>Gets the size Jellyfin makes, e.g. <c>720p</c>, or null for a decode test or when not measured.</summary>
    public string? OutputSize { get; init; }

    /// <summary>Gets a hash of the ffmpeg command measured, or null when nothing ran; equal hashes mean a setting didn't change the command.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Command { get; init; }

    /// <summary>Gets a value indicating whether a run was cut off by a timeout or the time limit; such a result isn't saved for reuse.</summary>
    [JsonIgnore]
    public bool Interrupted { get; init; }

    /// <summary>Gets when an earlier run measured it, when it was reused rather than measured again, or null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ReusedFromUtc { get; init; }

    /// <summary>Gets a value indicating whether it's planned and not measured yet; only in a running run's results.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Pending { get; init; }
}

using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;

namespace Jellyfin.Plugin.HwProbe.Core.Verdict;

/// <summary>Remedy text per outcome, shown next to each failure in the report.</summary>
public static class Hints
{
    /// <summary>Remedy for a host that resolved to the copy-back filter pipeline.</summary>
    public const string LegacyCopyBack =
        "Filters run in software (copy-back). Install the OpenCL runtime (intel-opencl-icd on Intel) to reach the full hardware pipeline.";

    /// <summary>Returns the remedy for an Intel device whose OpenCL runtime doesn't start.</summary>
    /// <param name="inContainer">Whether the probe ran inside a container.</param>
    /// <returns>Remedy text.</returns>
    /// <remarks>
    /// The official image installs intel-opencl-icd, and intel-opencl-icd-legacy1 for "&lt;= Gen11 graphics" (jellyfin-packaging,
    /// docker/Dockerfile). The linuxserver mod installs both at container start (docker-mods, jellyfin-opencl-intel branch); on one
    /// Synology host its packages were present but never installed, so the log check matters.
    /// </remarks>
    public static string OpenclUnavailable(bool inContainer) => inContainer
        ? "Install Intel's OpenCL runtime in the container. The official jellyfin/jellyfin image includes it (intel-opencl-icd, and intel-opencl-icd-legacy1 for Gen 11 and older GPUs). On linuxserver/jellyfin, set DOCKER_MODS=linuxserver/mods:jellyfin-opencl-intel and check the container's start-up log shows the packages installing."
        : "Install Intel's OpenCL runtime: intel-opencl-icd, or intel-opencl-icd-legacy1 for Gen 11 and older GPUs.";

    /// <summary>Returns a short fix for a backend that doesn't work, when the user can act on it.</summary>
    /// <param name="verdict">The backend's verdict.</param>
    /// <param name="type">The backend.</param>
    /// <param name="os">The host OS.</param>
    /// <param name="inContainer">Whether the probe ran inside a container.</param>
    /// <returns>The fix, or null when there's nothing to do (e.g. the hardware isn't there or the build lacks it).</returns>
    public static Fix? FixFor(BackendVerdict verdict, HwType type, HostOs os, bool inContainer) => (verdict, type) switch
    {
        (BackendVerdict.PermissionDenied, HwType.vaapi or HwType.qsv) when inContainer =>
            new("Add the render group (--group-add render)", Section(type, "official-docker")),
        (BackendVerdict.PermissionDenied, HwType.vaapi or HwType.qsv) =>
            new("Add jellyfin to the render group (usermod)", Section(type, "configure-on-linux-host")),
        (BackendVerdict.NotPresent, HwType.nvenc) when inContainer =>
            new("Pass the GPU to the container (--gpus all)", JellyfinDocs.Guide("nvidia", "official-docker")),
        (BackendVerdict.NotPresent, HwType.vaapi or HwType.qsv) when inContainer =>
            new("Pass the GPU to the container (--device /dev/dri)", Section(type, "official-docker")),
        (BackendVerdict.NotPresent, HwType.vaapi or HwType.qsv) when os == HostOs.Linux =>
            new("Load the GPU driver", Section(type, "configure-on-linux-host")),
        _ => null,
    };

    /// <summary>Returns the fix for an Intel device whose OpenCL runtime doesn't start.</summary>
    /// <param name="inContainer">Whether the probe ran inside a container.</param>
    /// <returns>The fix.</returns>
    public static Fix OpenclFix(bool inContainer) => inContainer
        ? new("Install the Intel OpenCL runtime", JellyfinDocs.Guide("intel", "official-docker"))
        : new("Install the Intel OpenCL runtime (intel-opencl-icd)", JellyfinDocs.Guide("intel", "configure-on-linux-host"));

    /// <summary>Returns the remedy for an outcome.</summary>
    /// <param name="outcome">The probe outcome.</param>
    /// <param name="type">The backend probed.</param>
    /// <param name="os">The host OS, so remedies name tools that exist there.</param>
    /// <param name="inContainer">Whether the probe ran inside a container.</param>
    /// <returns>Remedy text; empty for a pass.</returns>
    public static string For(ProbeOutcome outcome, HwType type, HostOs os, bool inContainer) => outcome switch
    {
        ProbeOutcome.Pass => string.Empty,
        ProbeOutcome.PermissionDenied => PermissionDenied(type, inContainer),
        ProbeOutcome.DeviceUnavailable => DeviceUnavailable(type, os, inContainer),
        ProbeOutcome.CodecUnsupported =>
            "This GPU or driver does not support this codec. Leave it unticked in Jellyfin's hardware decoding list.",
        ProbeOutcome.FilterUnsupported =>
            "A filter in the pipeline failed. The driver may lack a feature; see the stderr tail.",
        ProbeOutcome.Timeout =>
            "ffmpeg hung and was killed, which usually means a wedged driver. Retry with no transcodes running.",
        ProbeOutcome.SoftwareFallback =>
            "ffmpeg finished but decoded in software. Jellyfin would silently use the CPU here.",
        ProbeOutcome.Skipped =>
            "Not run: a prerequisite is missing (a software encoder for the fixture, or the build lacks it).",
        ProbeOutcome.Untested =>
            "Not verified: no hardware or sample was available to test this.",
        ProbeOutcome.NotUsed =>
            "Jellyfin uses software for this with this ffmpeg build, so the hardware option has no effect.",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    /// <summary>Returns a section of the guide for a backend.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="anchor">The section anchor in the Intel guide.</param>
    /// <returns>The Intel guide section for QSV; the overview for VAAPI, which serves both Intel and AMD and has no such sections.</returns>
    private static Uri Section(HwType type, string anchor) =>
        type == HwType.qsv ? JellyfinDocs.Guide("intel", anchor) : JellyfinDocs.Guide(string.Empty);

    /// <summary>Remedy for a device this user cannot open.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="inContainer">Whether the probe ran inside a container.</param>
    /// <returns>Remedy text.</returns>
    private static string PermissionDenied(HwType type, bool inContainer) => type switch
    {
        HwType.vaapi or HwType.qsv when inContainer =>
            "The render node is not accessible. Add the host's render group to the container (--group-add).",
        HwType.vaapi or HwType.qsv =>
            "Add the jellyfin user to the render group (usermod -aG render jellyfin), then restart Jellyfin.",
        _ => "The device exists but this user cannot open it.",
    };

    /// <summary>Remedy for a device that could not be opened at all.</summary>
    /// <param name="type">The backend.</param>
    /// <param name="os">The host OS.</param>
    /// <param name="inContainer">Whether the probe ran inside a container.</param>
    /// <returns>Remedy text.</returns>
    private static string DeviceUnavailable(HwType type, HostOs os, bool inContainer) => type switch
    {
        HwType.qsv when os == HostOs.Windows =>
            "No Direct3D 11 adapter opened. Check the Intel graphics driver is installed.",
        HwType.amf =>
            "No Direct3D 11 adapter opened. Check the AMD graphics driver is installed.",
        HwType.nvenc when inContainer =>
            "ffmpeg has CUDA but no NVIDIA device opened. Run the container with the NVIDIA runtime (--gpus all).",
        HwType.nvenc =>
            "ffmpeg has CUDA but no NVIDIA device opened. Check the NVIDIA driver is installed (nvidia-smi).",
        HwType.vaapi or HwType.qsv when inContainer =>
            "No render node opened. Pass it into the container (--device /dev/dri/renderD128).",
        HwType.vaapi or HwType.qsv =>
            "No render node opened. Check the GPU driver is loaded (ls /dev/dri).",
        _ => "The device could not be opened. Check the GPU driver is installed.",
    };
}

using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Pipeline;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Jellyfin.Tests;

/// <summary>Exact Linux args from EncodingHelper 12.2.0 over recorded jellyfin-ffmpeg 8.1.2 capabilities.</summary>
/// <remarks>
/// Blessed from a run in the dotnet SDK container. A failure means upstream changed what it emits: review the
/// diff, then re-bless. EncodingHelper only takes these branches on Linux.
/// </remarks>
[Trait("Category", "Unit")]
[Collection(TestCollections.EncodingHelperEnvironment)]
public sealed class LinuxDriftTests
{
    // EncodingHelper uses the bare path only when File.Exists(renderNode), so the test creates it.
    private const string Node = "/tmp/hwprobe-renderD128";
    private const string Amd64 = "jellyfin-8.1.2-linux-amd64";
    private const string Arm64 = "jellyfin-8.1.2-linux-arm64";
    private const string Sdr = "setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709";
    private const string Hdr = "setparams=color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc";
    private const string OpenclTonemap = "tonemap_opencl=format=nv12:p=bt709:t=bt709:m=bt709:tonemap=bt2390:peak=100:desat=0";
    private const string VaapiDecode = "-hwaccel vaapi -hwaccel_output_format vaapi -noautorotate";
    private const string CudaDecode = "-hwaccel cuda -hwaccel_output_format cuda -noautorotate -hwaccel_flags +unsafe_output -threads 1";

    private static readonly Dictionary<string, Case> _cases = new(StringComparer.Ordinal)
    {
        ["vaapi-ihd-smoke"] = new(
            Amd64,
            VaapiDriver.IntelIhd,
            HwType.vaapi,
            Node,
            Hdr10: false,
            $"-init_hw_device vaapi=va:{Node},driver=iHD {VaapiDecode}",
            $" -vf \"{Sdr},scale_vaapi=w=320:h=180:format=nv12:extra_hw_frames=24\"",
            "h264_vaapi",
            string.Empty),
        ["vaapi-ihd-hdr10"] = new(
            Amd64,
            VaapiDriver.IntelIhd,
            HwType.vaapi,
            Node,
            Hdr10: true,
            $"-init_hw_device vaapi=va:{Node},driver=iHD {VaapiDecode}",
            $" -vf \"{Hdr},hwmap=derive_device=opencl:mode=read,{OpenclTonemap},hwmap=derive_device=vaapi:mode=write:reverse=1,format=vaapi\"",
            "h264_vaapi",
            string.Empty),
        ["vaapi-i965-smoke"] = new(
            Amd64,
            VaapiDriver.IntelI965,
            HwType.vaapi,
            Node,
            Hdr10: false,
            $"-init_hw_device vaapi=va:{Node},driver=i965 {VaapiDecode}",
            $" -vf \"{Sdr},scale_vaapi=w=320:h=180:format=nv12:extra_hw_frames=24\"",
            "h264_vaapi",
            "LIBVA_DRIVER_NAME=i965,LIBVA_DRIVER_NAME_JELLYFIN=i965"),
        ["vaapi-amd-smoke"] = new(
            Amd64,
            VaapiDriver.Amd,
            HwType.vaapi,
            Node,
            Hdr10: false,
            $"-init_hw_device vaapi=va:{Node} -filter_hw_device va {VaapiDecode}",
            $" -vf \"{Sdr},scale_vaapi=w=320:h=180:format=nv12:extra_hw_frames=24\"",
            "h264_vaapi",
            "AMD_DEBUG=noefc"),
        ["vaapi-amd-hdr10"] = new(
            Amd64,
            VaapiDriver.Amd,
            HwType.vaapi,
            Node,
            Hdr10: true,
            $"-init_hw_device vaapi=va:{Node} -init_hw_device opencl=ocl:.0,device_vendor=\"Advanced Micro Devices\" -filter_hw_device ocl {VaapiDecode}",
            $" -vf \"{Hdr},hwdownload,format=p010le,hwupload=derive_device=opencl,{OpenclTonemap},hwdownload,format=nv12,hwupload_vaapi\"",
            "h264_vaapi",
            "AMD_DEBUG=noefc"),
        ["qsv-smoke"] = new(
            Amd64,
            VaapiDriver.IntelIhd,
            HwType.qsv,
            Node,
            Hdr10: false,
            $"-init_hw_device vaapi=va:{Node},driver=iHD -init_hw_device qsv=qs@va -filter_hw_device qs {VaapiDecode}",
            $" -vf \"{Sdr},scale_vaapi=w=320:h=180:format=nv12:extra_hw_frames=24,hwmap=derive_device=qsv,format=qsv\"",
            "h264_qsv",
            string.Empty),
        ["qsv-hdr10"] = new(
            Amd64,
            VaapiDriver.IntelIhd,
            HwType.qsv,
            Node,
            Hdr10: true,
            $"-init_hw_device vaapi=va:{Node},driver=iHD -init_hw_device qsv=qs@va -init_hw_device opencl=ocl@va -filter_hw_device qs {VaapiDecode}",
            $" -vf \"{Hdr},hwmap=derive_device=opencl:mode=read,{OpenclTonemap},hwmap=derive_device=qsv:mode=write:reverse=1:extra_hw_frames=16,format=qsv\"",
            "h264_qsv",
            string.Empty),
        ["nvenc-smoke"] = new(
            Amd64,
            VaapiDriver.Other,
            HwType.nvenc,
            "0",
            Hdr10: false,
            $"-init_hw_device cuda=cu:0 -filter_hw_device cu {CudaDecode}",
            $" -vf \"{Sdr},scale_cuda=w=320:h=180:format=yuv420p\"",
            "h264_nvenc",
            string.Empty),
        ["nvenc-hdr10"] = new(
            Amd64,
            VaapiDriver.Other,
            HwType.nvenc,
            "0",
            Hdr10: true,
            $"-init_hw_device cuda=cu:0 -filter_hw_device cu {CudaDecode}",
            $" -vf \"{Hdr},tonemap_cuda=format=yuv420p:p=bt709:t=bt709:m=bt709:tonemap=bt2390:peak=100:desat=0\"",
            "h264_nvenc",
            string.Empty),
        ["rkmpp-smoke"] = new(
            Arm64,
            VaapiDriver.Other,
            HwType.rkmpp,
            null,
            Hdr10: false,
            "-init_hw_device rkmpp=rk -hwaccel rkmpp -hwaccel_output_format drm_prime -noautorotate -afbc rga",
            $" -vf \"{Sdr},vpp_rkrga=w=320:h=180:format=nv12:afbc=1\"",
            "h264_rkmpp",
            string.Empty),
    };

    /// <summary>Generated args match the blessed strings exactly.</summary>
    /// <param name="name">The case.</param>
    /// <returns>A task representing the test.</returns>
    [Theory(Skip = "Requires Linux: EncodingHelper only takes these branches there.", SkipUnless = nameof(TestEnvironment.IsLinux), SkipType = typeof(TestEnvironment))]
    [InlineData("vaapi-ihd-smoke")]
    [InlineData("vaapi-ihd-hdr10")]
    [InlineData("vaapi-i965-smoke")]
    [InlineData("vaapi-amd-smoke")]
    [InlineData("vaapi-amd-hdr10")]
    [InlineData("qsv-smoke")]
    [InlineData("qsv-hdr10")]
    [InlineData("nvenc-smoke")]
    [InlineData("nvenc-hdr10")]
    [InlineData("rkmpp-smoke")]
    public async Task MatchesBlessedArgs(string name)
    {
        await File.WriteAllTextAsync(Node, string.Empty, TestContext.Current.CancellationToken);
        var c = _cases[name];
        var cell = c.Hdr10 ? MatrixCatalog.For(c.Type).Single(m => m.Group == MatrixGroup.Tonemap && !m.Cell.VppTonemap).Cell : MatrixCatalog.Smoke.Cell;
        var recorder = new CallRecorder();

        var args = new ArgumentSource(await CorpusCapabilities.LoadAsync(c.Build, c.Driver), recorder).Build(c.Type, c.Device, cell);

        Assert.Equal(c.Input, args.InputArgs);
        Assert.Equal(c.Filters, args.FilterArgs);
        Assert.Equal(c.Encoder, args.VideoEncoder);
        Assert.Equal(c.Environment, string.Join(",", args.Environment.Where(kv => kv.Value is not null).Select(kv => $"{kv.Key}={kv.Value}")));
        Assert.NotNull(args.HardwareDecoder);
        Assert.True(args.HardwareEncoder);
        Assert.Equal(c.Hdr10, args.HardwareTonemap);
        Assert.Empty(recorder.Unexpected);
    }

    /// <summary>One blessed case.</summary>
    /// <param name="Build">Recorded corpus directory.</param>
    /// <param name="Driver">VAAPI driver from the device open.</param>
    /// <param name="Type">Backend.</param>
    /// <param name="Device">Device selector.</param>
    /// <param name="Hdr10">Whether the cell is the HDR10 tone-map cell.</param>
    /// <param name="Input">Expected input args.</param>
    /// <param name="Filters">Expected filter args.</param>
    /// <param name="Encoder">Expected encoder.</param>
    /// <param name="Environment">Expected set variables for the child, as <c>K=V,K=V</c>.</param>
    private sealed record Case(string Build, VaapiDriver Driver, HwType Type, string? Device, bool Hdr10, string Input, string Filters, string Encoder, string Environment);
}

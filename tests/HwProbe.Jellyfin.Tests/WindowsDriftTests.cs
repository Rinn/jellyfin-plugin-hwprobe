using System.Text.Json;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Jellyfin.Tests;

/// <summary>Exact Windows args from EncodingHelper 12.1.0 over recorded jellyfin-ffmpeg 8.1.3 capabilities.</summary>
/// <remarks>
/// A failure means upstream changed what it emits: review the diff, then re-bless on Windows by setting
/// HWPROBE_BLESS to a file path, which writes the actual args there instead of asserting.
/// EncodingHelper only takes these branches on Windows.
/// </remarks>
[Trait("Category", "Unit")]
[Trait("Category", "Platform")]
[Collection(TestCollections.EncodingHelperEnvironment)]
public sealed class WindowsDriftTests
{
    private const string Build = "jellyfin-8.1.3-windows-x64";
    private const string Sdr = "setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709";
    private const string Hdr = "setparams=color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc";
    private const string OpenclTonemap = "tonemap_opencl=format=nv12:p=bt709:t=bt709:m=bt709:tonemap=bt2390:peak=100:desat=0";
    private const string D3d11Decode = "-hwaccel d3d11va -hwaccel_output_format d3d11 -noautorotate -threads 2";
    private const string CudaInput = "-init_hw_device cuda=cu:0 -filter_hw_device cu -hwaccel cuda -hwaccel_output_format cuda -noautorotate -hwaccel_flags +unsafe_output -threads 1";
    private const string AmfInput = $"-init_hw_device d3d11va=dx11:,vendor=0x1002 -init_hw_device opencl=ocl@dx11 -filter_hw_device ocl {D3d11Decode}";

    private static readonly Dictionary<string, Case> _cases = new(StringComparer.Ordinal)
    {
        ["amf-smoke"] = new(
            HwType.amf,
            Hdr10: false,
            AmfInput,
            $" -vf \"{Sdr},hwmap=derive_device=opencl:mode=read,scale_opencl=w=320:h=180:format=nv12,hwmap=derive_device=d3d11va:mode=write:reverse=1,format=d3d11\"",
            "h264_amf"),
        ["amf-hdr10"] = new(
            HwType.amf,
            Hdr10: true,
            AmfInput,
            $" -vf \"{Hdr},hwmap=derive_device=opencl:mode=read,{OpenclTonemap},hwmap=derive_device=d3d11va:mode=write:reverse=1,format=d3d11\"",
            "h264_amf"),
        ["nvenc-smoke"] = new(
            HwType.nvenc,
            Hdr10: false,
            CudaInput,
            $" -vf \"{Sdr},scale_cuda=w=320:h=180:format=yuv420p\"",
            "h264_nvenc"),
        ["nvenc-hdr10"] = new(
            HwType.nvenc,
            Hdr10: true,
            CudaInput,
            $" -vf \"{Hdr},tonemap_cuda=format=yuv420p:p=bt709:t=bt709:m=bt709:tonemap=bt2390:peak=100:desat=0\"",
            "h264_nvenc"),
        ["qsv-smoke"] = new(
            HwType.qsv,
            Hdr10: false,
            $"-init_hw_device d3d11va=dx11:0 -init_hw_device qsv=qs@dx11 -filter_hw_device qs {D3d11Decode}",
            $" -vf \"{Sdr},hwmap=derive_device=qsv,vpp_qsv=w=320:h=180:format=nv12:passthrough=0\"",
            "h264_qsv"),
        ["qsv-hdr10"] = new(
            HwType.qsv,
            Hdr10: true,
            $"-init_hw_device d3d11va=dx11:0 -init_hw_device qsv=qs@dx11 -init_hw_device opencl=ocl@dx11 -filter_hw_device qs {D3d11Decode}",
            $" -vf \"{Hdr},hwmap=derive_device=opencl:mode=read,{OpenclTonemap},hwmap=derive_device=qsv:mode=write:reverse=1,format=qsv\"",
            "h264_qsv"),
    };

    /// <summary>The generated args match the blessed strings.</summary>
    /// <param name="name">The case.</param>
    /// <returns>A task representing the test.</returns>
    [Theory(Skip = "Requires Windows: EncodingHelper only takes these branches there.", SkipUnless = nameof(TestEnvironment.IsWindows), SkipType = typeof(TestEnvironment))]
    [InlineData("amf-smoke")]
    [InlineData("amf-hdr10")]
    [InlineData("nvenc-smoke")]
    [InlineData("nvenc-hdr10")]
    [InlineData("qsv-smoke")]
    [InlineData("qsv-hdr10")]
    public async Task MatchesBlessedArgs(string name)
    {
        var c = _cases[name];
        var cell = c.Hdr10 ? MatrixCatalog.For(c.Type).Single(m => m.Group == MatrixGroup.Tonemap && !m.Cell.VppTonemap).Cell : MatrixCatalog.Smoke.Cell;
        var recorder = new CallRecorder();
        var args = new ArgumentSource(await CorpusCapabilities.LoadAsync(Build), recorder).Build(c.Type, "0", cell);

        if (Environment.GetEnvironmentVariable("HWPROBE_BLESS") is { Length: > 0 } bless)
        {
            var line = JsonSerializer.Serialize(new { name, args.InputArgs, args.FilterArgs, args.VideoEncoder, args.HardwareTonemap, Unexpected = recorder.Unexpected.Count });
            await File.AppendAllTextAsync(bless, line + Environment.NewLine, TestContext.Current.CancellationToken);
            return;
        }

        Assert.Equal(c.Input, args.InputArgs);
        Assert.Equal(c.Filters, args.FilterArgs);
        Assert.Equal(c.Encoder, args.VideoEncoder);
        Assert.NotNull(args.HardwareDecoder);
        Assert.True(args.HardwareEncoder);
        Assert.Equal(c.Hdr10, args.HardwareTonemap);
        Assert.Empty(recorder.Unexpected);
    }

    /// <summary>One blessed case.</summary>
    /// <param name="Type">Backend.</param>
    /// <param name="Hdr10">Whether the cell is the HDR10 tone-map cell.</param>
    /// <param name="Input">Expected input args.</param>
    /// <param name="Filters">Expected filter args.</param>
    /// <param name="Encoder">Expected encoder.</param>
    private sealed record Case(HwType Type, bool Hdr10, string Input, string Filters, string Encoder);
}

using Jellyfin.Plugin.HwProbe.Core.Data;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Probes;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Jellyfin.Tests;

/// <summary>Inside the server, generation never sets a variable the server doesn't, and never undoes one.</summary>
/// <remarks>EncodingHelper only takes its VAAPI branch on Linux, so these only run there.</remarks>
[Trait("Category", "Unit")]
[Collection(TestCollections.EncodingHelperEnvironment)]
public sealed class InServerEnvironmentTests : IDisposable
{
    private const string Node = "/tmp/hwprobe-renderD128";
    private const string Skip = "Requires Linux: EncodingHelper only takes its VAAPI branch there.";

    private static readonly ProbeCell _smoke = MatrixCatalog.Smoke.Cell;
    private static readonly Dictionary<string, string?> _cleanBaseline = new()
    {
        ["LIBVA_DRIVER_NAME"] = null,
        ["LIBVA_DRIVER_NAME_JELLYFIN"] = null,
        ["AMD_DEBUG"] = null,
    };

    private readonly IReadOnlyDictionary<string, string?> _before = EncodingHelperEnvironment.Capture();

    /// <summary>Initializes a new instance of the <see cref="InServerEnvironmentTests"/> class with a clean environment.</summary>
    public InServerEnvironmentTests()
    {
        File.WriteAllText(Node, string.Empty);
        SetAll(_cleanBaseline);
    }

    /// <summary>An i965 device the server isn't configured for is refused before anything is written.</summary>
    [Fact(Skip = Skip, SkipUnless = nameof(TestEnvironment.IsLinux), SkipType = typeof(TestEnvironment))]
    public void ForeignWriteIsRefused()
    {
        var source = Source(intelI965: true, serverOwned: new Dictionary<string, string>());

        var ex = Assert.Throws<UnsafeProbeException>(() => source.Build(HwType.vaapi, Node, _smoke));

        Assert.Equal(Catalog.Text("notServerDevice", ("device", Node)), ex.Message);
        Assert.Equal(_cleanBaseline, EncodingHelperEnvironment.Capture());
    }

    /// <summary>A write the server makes itself is allowed and left in place, since the server never undoes it.</summary>
    [Fact(Skip = Skip, SkipUnless = nameof(TestEnvironment.IsLinux), SkipType = typeof(TestEnvironment))]
    public void ServerOwnedWriteIsKept()
    {
        var owned = EncodingHelperEnvironment.Predict(HwType.vaapi, intelIhd: false, intelI965: true, amd: false);
        var source = Source(intelI965: true, serverOwned: owned);

        var args = source.Build(HwType.vaapi, Node, _smoke);

        Assert.Equal("i965", args.Environment["LIBVA_DRIVER_NAME"]);
        Assert.Equal("i965", Environment.GetEnvironmentVariable("LIBVA_DRIVER_NAME"));
        Assert.Null(Environment.GetEnvironmentVariable("AMD_DEBUG"));
    }

    /// <summary>A probe's ffmpeg gets the start-up values, not whatever the server has set since.</summary>
    [Fact(Skip = Skip, SkipUnless = nameof(TestEnvironment.IsLinux), SkipType = typeof(TestEnvironment))]
    public void ChildGetsBaselineNotCurrentProcessValues()
    {
        // As if the server's own i965 transcodes had run since start-up.
        Environment.SetEnvironmentVariable("LIBVA_DRIVER_NAME", "i965");
        var source = Source(intelI965: false, serverOwned: new Dictionary<string, string>());

        var args = source.Build(HwType.vaapi, Node, _smoke);

        Assert.Null(args.Environment["LIBVA_DRIVER_NAME"]);
        Assert.Equal("i965", Environment.GetEnvironmentVariable("LIBVA_DRIVER_NAME"));
    }

    /// <inheritdoc/>
    public void Dispose() => SetAll(_before);

    /// <summary>Sets every guarded variable.</summary>
    /// <param name="values">Values; null removes.</param>
    private static void SetAll(IReadOnlyDictionary<string, string?> values)
    {
        foreach (var (name, value) in values)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    /// <summary>Creates an argument source with in-server rules over the recorded amd64 build.</summary>
    /// <param name="intelI965">Whether the probed device reports i965 (otherwise iHD).</param>
    /// <param name="serverOwned">Variables the server's configuration sets.</param>
    /// <returns>The argument source.</returns>
    private static ArgumentSource Source(bool intelI965, IReadOnlyDictionary<string, string> serverOwned)
    {
        var caps = TestCapabilities.Full with { IsVaapiDeviceInteliHD = !intelI965, IsVaapiDeviceInteli965 = intelI965 };
        return new ArgumentSource(caps, new CallRecorder(), EnvironmentRules.InServer(_cleanBaseline, serverOwned));
    }
}

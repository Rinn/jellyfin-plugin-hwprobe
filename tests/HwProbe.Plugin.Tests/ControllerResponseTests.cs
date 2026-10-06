using Jellyfin.Plugin.HwProbe.Api;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Probing;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.PluginTests;

/// <summary>Controller responses over a <see cref="ProbeService"/> that saves its report to disk.</summary>
[Trait("Category", "Unit")]
[Trait("Category", "Platform")]
public sealed class ControllerResponseTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("hwprobe-shell-").FullName;

    /// <summary>Run answers 202 when idle and 409 while transcoding.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task RunMapsResultsToStatusCodes()
    {
        using var release = new SemaphoreSlim(0);
        using var idle = Service(
            transcoding: false,
            async ct =>
            {
                await release.WaitAsync(ct);
                return Reports.Sample();
            });
        using var busy = Service(transcoding: true, _ => Task.FromResult(Reports.Sample()));
        using var settings = new SettingsHarness();

        Assert.IsType<AcceptedResult>(await new HwProbeController(idle, settings.Service).RunAsync(TestContext.Current.CancellationToken));
        Assert.IsType<ConflictObjectResult>(await new HwProbeController(idle, settings.Service).RunAsync(TestContext.Current.CancellationToken));
        Assert.IsType<ConflictObjectResult>(await new HwProbeController(busy, settings.Service).RunAsync(TestContext.Current.CancellationToken));
        release.Release();

        // The started probe writes its report in the background; cleanup must not race it.
        await idle.Background;
    }

    /// <summary>Report answers 404 before any probe and the saved JSON after one.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ReportIsNotFoundThenContent()
    {
        using var service = Service(transcoding: false, _ => Task.FromResult(Reports.Sample()));
        using var settings = new SettingsHarness();
        var controller = new HwProbeController(service, settings.Service);

        Assert.IsType<NotFoundResult>(await controller.GetReportAsync(TestContext.Current.CancellationToken));

        await service.RunAsync(TestContext.Current.CancellationToken);
        var content = Assert.IsType<ContentResult>(await controller.GetReportAsync(TestContext.Current.CancellationToken));
        Assert.Equal("application/json", content.ContentType);
        Assert.Contains($"\"schemaVersion\": {CapabilityReport.CurrentSchemaVersion}", content.Content, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>Creates a service with a scripted probe.</summary>
    /// <param name="transcoding">Whether a session is transcoding.</param>
    /// <param name="probe">The probe.</param>
    /// <returns>The service.</returns>
    private ProbeService Service(bool transcoding, Func<CancellationToken, Task<CapabilityReport>> probe) =>
        new(probe, () => transcoding, Path.Combine(_directory, $"{Guid.NewGuid():N}.json"), TimeProvider.System, TimeSpan.Zero, NullLogger.Instance);
}

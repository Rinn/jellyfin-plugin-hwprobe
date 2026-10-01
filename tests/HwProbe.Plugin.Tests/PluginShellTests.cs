using Jellyfin.Plugin.HwProbe.Api;
using Jellyfin.Plugin.HwProbe.Core.Report;
using Jellyfin.Plugin.HwProbe.Probing;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.PluginTests;

/// <summary>Controller responses, task behaviour and the embedded page.</summary>
[Trait("Category", "Unit")]
public sealed class PluginShellTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("hwprobe-shell-").FullName;

    /// <summary>The controller requires an administrator.</summary>
    [Fact]
    public void ControllerRequiresElevation()
    {
        var authorize = Assert.Single(typeof(HwProbeController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>());

        Assert.Equal(Policies.RequiresElevation, authorize.Policy);
    }

    /// <summary>Run answers 202 when idle and 409 while transcoding.</summary>
    [Fact]
    public void RunMapsResultsToStatusCodes()
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

        Assert.IsType<AcceptedResult>(new HwProbeController(idle).Run());
        Assert.IsType<ConflictObjectResult>(new HwProbeController(idle).Run());
        Assert.IsType<ConflictObjectResult>(new HwProbeController(busy).Run());
        release.Release();
    }

    /// <summary>Report answers 404 before any probe and the saved JSON after one.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ReportIsNotFoundThenContent()
    {
        using var service = Service(transcoding: false, _ => Task.FromResult(Reports.Sample()));
        var controller = new HwProbeController(service);

        Assert.IsType<NotFoundResult>(await controller.GetReportAsync(TestContext.Current.CancellationToken));

        await service.RunAsync(TestContext.Current.CancellationToken);
        var content = Assert.IsType<ContentResult>(await controller.GetReportAsync(TestContext.Current.CancellationToken));
        Assert.Equal("application/json", content.ContentType);
        Assert.Contains("\"schemaVersion\": 1", content.Content, StringComparison.Ordinal);
    }

    /// <summary>The task fails visibly when the probe fails, and has no default trigger.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task TaskSurfacesFailure()
    {
        using var service = Service(transcoding: false, _ => throw new InvalidOperationException("no ffmpeg"));
        var task = new HardwareProbeTask(service);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task.ExecuteAsync(new Progress<double>(), TestContext.Current.CancellationToken));
        Assert.Equal("no ffmpeg", ex.Message);
        Assert.Empty(task.GetDefaultTriggers());
    }

    /// <summary>The configuration page is embedded where GetPages points.</summary>
    [Fact]
    public void ConfigPageIsEmbedded()
    {
        var assembly = typeof(HwProbe.Plugin).Assembly;
        var resource = $"{typeof(HwProbe.Plugin).Namespace}.Configuration.configPage.html";

        using var stream = assembly.GetManifestResourceStream(resource);
        Assert.NotNull(stream);
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>Creates a service with a scripted probe.</summary>
    /// <param name="transcoding">Whether a session is transcoding.</param>
    /// <param name="probe">The probe.</param>
    /// <returns>The service.</returns>
    private ProbeService Service(bool transcoding, Func<CancellationToken, Task<CapabilityReport>> probe) =>
        new(probe, () => transcoding, Path.Combine(_directory, $"{Guid.NewGuid():N}.json"), TimeProvider.System, NullLogger.Instance);
}

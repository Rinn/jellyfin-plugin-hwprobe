using System.Text.Json;
using Jellyfin.Plugin.HwProbe.Api;
using Jellyfin.Plugin.HwProbe.Core.Model;
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

        Assert.IsType<AcceptedResult>(new HwProbeController(idle, settings.Service).Run());
        Assert.IsType<ConflictObjectResult>(new HwProbeController(idle, settings.Service).Run());
        Assert.IsType<ConflictObjectResult>(new HwProbeController(busy, settings.Service).Run());
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
        Assert.Contains("\"schemaVersion\": 3", content.Content, StringComparison.Ordinal);
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

    /// <summary>The configuration page shows every per-codec column of the report, and the settings advice.</summary>
    [Fact]
    public void ConfigPageShowsEveryColumn()
    {
        var assembly = typeof(HwProbe.Plugin).Assembly;
        using var stream = assembly.GetManifestResourceStream($"{typeof(HwProbe.Plugin).Namespace}.Configuration.configPage.html")!;
        using var reader = new StreamReader(stream);
        var page = reader.ReadToEnd();
        var columns = typeof(BackendReport).GetProperties()
            .Where(p => p.PropertyType == typeof(IReadOnlyDictionary<string, ProbeOutcome>))
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .ToList();

        Assert.Equal(5, columns.Count);
        Assert.All(columns, c => Assert.Contains($"'{c}'", page, StringComparison.Ordinal));
        Assert.Contains("selected.settings", page, StringComparison.Ordinal);
    }

    /// <summary>The page has no "${", which jellyfin-web's translateHtml would replace with a translation lookup.</summary>
    [Fact]
    public void ConfigPageHasNoTranslationPlaceholders()
    {
        using var stream = typeof(HwProbe.Plugin).Assembly.GetManifestResourceStream($"{typeof(HwProbe.Plugin).Namespace}.Configuration.configPage.html")!;
        using var reader = new StreamReader(stream);

        Assert.DoesNotContain("${", reader.ReadToEnd(), StringComparison.Ordinal);
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

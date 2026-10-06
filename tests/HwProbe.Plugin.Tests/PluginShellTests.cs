using System.Text.Json;
using Jellyfin.Plugin.HwProbe.Api;
using Jellyfin.Plugin.HwProbe.Core.Model;
using Jellyfin.Plugin.HwProbe.Core.Report;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.PluginTests;

/// <summary>Controller authorization and the embedded page.</summary>
[Trait("Category", "Unit")]
public sealed class PluginShellTests
{
    /// <summary>The controller requires an administrator.</summary>
    [Fact]
    public void ControllerRequiresElevation()
    {
        var authorize = Assert.Single(typeof(HwProbeController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>());

        Assert.Equal(Policies.RequiresElevation, authorize.Policy);
    }

    /// <summary>The configuration page shows every per-codec column of the report, and the settings advice.</summary>
    [Fact]
    public void ConfigPageShowsEveryColumn()
    {
        var page = ReadPage();
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
        Assert.DoesNotContain("${", ReadPage(), StringComparison.Ordinal);
    }

    /// <summary>Reads the configuration page from where GetPages points, failing when it isn't embedded there.</summary>
    /// <returns>The page.</returns>
    private static string ReadPage()
    {
        using var stream = typeof(HwProbe.Plugin).Assembly.GetManifestResourceStream($"{typeof(HwProbe.Plugin).Namespace}.Configuration.configPage.html");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

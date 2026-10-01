using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Cli.Tests;

/// <summary>Guards the assembly naming that lets M2 drop in without a rename.</summary>
public sealed class AssemblyNamingTests
{
    /// <summary>The project under test builds as Jellyfin.Plugin.HwProbe.Cli.</summary>
    [Fact]
    [Trait("Category", "Unit")]
    public void ProjectUnderTestUsesPluginPrefix()
    {
        // Load by name: the compiler drops references to assemblies no code uses yet.
        var assembly = Assembly.Load(new AssemblyName("Jellyfin.Plugin.HwProbe.Cli"));

        Assert.Equal("Jellyfin.Plugin.HwProbe.Cli", assembly.GetName().Name);
    }
}

namespace Jellyfin.Plugin.HwProbe.Jellyfin.Tests;

/// <summary>xunit collection names; classes in one collection never run in parallel with each other.</summary>
internal static class TestCollections
{
    /// <summary>Classes that generate args, since EncodingHelper sets process env.</summary>
    public const string EncodingHelperEnvironment = "EncodingHelper environment";
}

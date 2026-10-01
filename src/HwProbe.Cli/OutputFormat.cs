namespace Jellyfin.Plugin.HwProbe.Cli;

/// <summary>Report rendering on stdout.</summary>
internal enum OutputFormat
{
    /// <summary>Human-readable table.</summary>
    Table,

    /// <summary>The JSON report.</summary>
    Json,
}

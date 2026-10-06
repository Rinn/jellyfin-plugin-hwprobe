namespace Jellyfin.Plugin.HwProbe.Probing;

/// <summary>How much one kind of saved file takes.</summary>
/// <param name="Files">Number of files.</param>
/// <param name="Bytes">Total size.</param>
/// <param name="ModifiedUtc">When the newest was written, or null when there are none.</param>
public sealed record SavedFilesSize(int Files, long Bytes, DateTimeOffset? ModifiedUtc);

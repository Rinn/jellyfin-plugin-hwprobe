namespace Jellyfin.Plugin.HwProbe.Core.Fixtures;

/// <summary>One cached clip, sample or download.</summary>
/// <param name="Folder">The cache folder: <c>samples</c>, <c>downloads</c>, or an ffmpeg build's fingerprint.</param>
/// <param name="File">The file name.</param>
/// <param name="Bytes">Its size, with its hash file.</param>
/// <param name="ModifiedUtc">When it was written.</param>
/// <param name="Description">What it is, or null when this version doesn't use it.</param>
public sealed record CacheEntry(string Folder, string File, long Bytes, DateTimeOffset ModifiedUtc, string? Description);

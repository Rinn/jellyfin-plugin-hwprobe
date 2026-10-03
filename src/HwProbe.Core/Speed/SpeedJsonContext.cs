using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Source-generated JSON for the speed report, shaped like the capability report's.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true, WriteIndented = true)]
[JsonSerializable(typeof(SpeedReport))]
[JsonSerializable(typeof(SpeedCacheEntry))]
internal sealed partial class SpeedJsonContext : JsonSerializerContext;

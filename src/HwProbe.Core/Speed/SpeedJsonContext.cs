using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>Source-generated JSON for the speed report and saved measurements, compact: runs and measurements are saved by the hundred.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SpeedReport))]
[JsonSerializable(typeof(SpeedCacheEntry))]
internal sealed partial class SpeedJsonContext : JsonSerializerContext
{
    /// <summary>The context for <see cref="Files"/>, made on first use: static initializers in the generated part may not have run yet.</summary>
    private static readonly Lazy<SpeedJsonContext> _files = new(CreateFiles);

    /// <summary>Gets the context that writes non-ASCII text such as → as is; the files are never embedded in HTML.</summary>
    public static SpeedJsonContext Files => _files.Value;

    /// <summary>Makes the <see cref="Files"/> context from the default one's options.</summary>
    /// <returns>The context.</returns>
    private static SpeedJsonContext CreateFiles() => new(new JsonSerializerOptions(Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.HwProbe.Core.Report;

/// <summary>Source-generated JSON for the report; enums as strings, camelCase members.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true, WriteIndented = true)]
[JsonSerializable(typeof(CapabilityReport))]
internal sealed partial class ReportJsonContext : JsonSerializerContext;

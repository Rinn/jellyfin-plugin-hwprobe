using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.HwProbe.FakeFfmpeg;

/// <summary>Source-generated JSON metadata for the scenario and log formats.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(FakeScenario))]
[JsonSerializable(typeof(FakeInvocation))]
internal sealed partial class FakeJsonContext : JsonSerializerContext;

using Jellyfin.Plugin.HwProbe.Core.Speed;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Cli.Tests;

/// <summary>Lines of <see cref="SuggestionRenderer"/>.</summary>
[Trait("Category", "Unit")]
public sealed class SuggestionRendererTests
{
    /// <summary>A recommended value gives the catalog's reason, and the values compared are listed with a serial comma.</summary>
    [Fact]
    public void NamesTheRecommendedValueAndListsTheOthers()
    {
        var text = SuggestionRenderer.Render(
        [
            new SpeedSuggestion(SpeedSuggestionKind.RecommendedValue, ["a"]) { Setting = "EncoderPreset", Value = "auto", Current = true, Others = ["faster", "fast"] },
            new SpeedSuggestion(SpeedSuggestionKind.FasterSetting, ["a"]) { Setting = "H264Crf", Value = "28", Others = ["18", "23", "26"], Gain = 0.5 },
        ]);

        Assert.Contains("Encoding preset: Auto (server setting), Jellyfin's default, which picks a preset for each encoder\n", text, StringComparison.Ordinal);
        Assert.Contains("H.264 encoding CRF: 28, 50% faster than 18, 23, or 26\n", text, StringComparison.Ordinal);
    }
}

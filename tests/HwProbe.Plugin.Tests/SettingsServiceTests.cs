using Jellyfin.Plugin.HwProbe.Settings;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.PluginTests;

/// <summary>Applying advice, switching backend, history and revert.</summary>
[Trait("Category", "Unit")]
public sealed class SettingsServiceTests : IDisposable
{
    private readonly SettingsHarness _harness = new();

    /// <summary>Changes matching the advice are saved; other options are left as they were.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ApplyWritesMatchingAdvice()
    {
        _harness.Saved.EncoderPreset = EncoderPreset.slow;

        var result = await ApplyAsync(("HardwareDecodingCodecs:hevc", true), ("AllowAv1Encoding", false));

        Assert.Equal(ApplyOutcome.Applied, result.Outcome);
        Assert.Equal(["h264", "hevc"], _harness.Saved.HardwareDecodingCodecs);
        Assert.False(_harness.Saved.AllowAv1Encoding);
        Assert.Equal(EncoderPreset.slow, _harness.Saved.EncoderPreset);
        Assert.Equal(1, _harness.Saves);
        Assert.False(result.RestartRequired);
        Assert.False(_harness.Service.RestartRequired);
        Assert.Equal(
            [
                new AppliedChange("HardwareDecodingCodecs:hevc", "false", "true") { Label = "HEVC" },
                new AppliedChange("AllowAv1Encoding", "true", "false") { Label = "Allow encoding in AV1 format" },
            ],
            result.Changes);
    }

    /// <summary>A value the advice doesn't support, an untested option or an unknown setting is refused and nothing is saved.</summary>
    /// <param name="setting">The setting.</param>
    /// <param name="value">The value.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("AllowAv1Encoding", true)]
    [InlineData("EnableTonemapping", true)]
    [InlineData("EncoderAppPath", true)]
    public async Task ApplyRefusesWhatTheReportDoesNotSupport(string setting, bool value)
    {
        var result = await ApplyAsync(("HardwareDecodingCodecs:hevc", true), (setting, value));

        Assert.Equal(ApplyOutcome.Rejected, result.Outcome);
        Assert.Equal(0, _harness.Saves);
    }

    /// <summary>Applying is refused without a report, for a different ffmpeg, for an unprobed backend, and while probing.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ApplyNeedsACurrentReportAndIdleServer()
    {
        _harness.Probing = true;
        Assert.Equal(ApplyOutcome.Busy, (await ApplyAsync(("AllowAv1Encoding", false))).Outcome);
        _harness.Probing = false;

        _harness.Saved.HardwareAccelerationType = HardwareAccelerationType.nvenc;
        Assert.Equal(ApplyOutcome.Rejected, (await ApplyAsync(("AllowAv1Encoding", false))).Outcome);
        _harness.Saved.HardwareAccelerationType = HardwareAccelerationType.vaapi;

        _harness.EncoderPath = "/usr/bin/ffmpeg";
        Assert.Equal(ApplyOutcome.Rejected, (await ApplyAsync(("AllowAv1Encoding", false))).Outcome);

        _harness.Report = null;
        Assert.Equal(ApplyOutcome.NoReport, (await ApplyAsync(("AllowAv1Encoding", false))).Outcome);
        Assert.Equal(0, _harness.Saves);
    }

    /// <summary>Values that already match are not saved or recorded.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task UnchangedValuesAreNotSaved()
    {
        _harness.Saved.AllowAv1Encoding = false;

        var result = await ApplyAsync(("AllowAv1Encoding", false));

        Assert.Equal(ApplyOutcome.Applied, result.Outcome);
        Assert.Empty(result.Changes);
        Assert.Equal(0, _harness.Saves);
        Assert.Empty(await _harness.Service.HistoryAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Switching to a viable backend sets its type and device and needs a restart; others are refused.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task UseBackendSwitchesToViableOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(ApplyOutcome.Rejected, (await _harness.Service.UseBackendAsync(new("nvenc", "0"), "admin", ct)).Outcome);
        Assert.Equal(ApplyOutcome.Rejected, (await _harness.Service.UseBackendAsync(new("qsv", "/dev/dri/renderD129"), "admin", ct)).Outcome);

        Assert.False(_harness.Service.RestartRequired);

        var result = await _harness.Service.UseBackendAsync(new("qsv", SettingsHarness.Node), "admin", ct);

        Assert.Equal(ApplyOutcome.Applied, result.Outcome);
        Assert.True(result.RestartRequired);
        Assert.True(_harness.Service.RestartRequired);
        Assert.Equal(HardwareAccelerationType.qsv, _harness.Saved.HardwareAccelerationType);
        Assert.Equal(SettingsHarness.Node, _harness.Saved.QsvDevice);
    }

    /// <summary>Reverting a backend switch changes the backend again, so a restart stays pending.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task RevertingABackendSwitchKeepsTheRestartPending()
    {
        var ct = TestContext.Current.CancellationToken;
        await _harness.Service.UseBackendAsync(new("qsv", SettingsHarness.Node), "admin", ct);
        Assert.True(_harness.Service.RestartRequired);

        var revert = await _harness.Service.RevertAsync("admin", ct);

        Assert.True(revert.RestartRequired);
        Assert.Equal(HardwareAccelerationType.vaapi, _harness.Saved.HardwareAccelerationType);
        Assert.True(_harness.Service.RestartRequired);
    }

    /// <summary>A trickplay option is saved to the trickplay settings only, and reverts like any other.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task TrickplayIsSavedSeparately()
    {
        var result = await ApplyAsync(("Trickplay:EnableHwAcceleration", true));

        Assert.Equal([new AppliedChange("Trickplay:EnableHwAcceleration", "false", "true") { Label = "Trickplay: Enable hardware decoding" }], result.Changes);
        Assert.True(_harness.SavedTrickplay.EnableHwAcceleration);
        Assert.Equal(0, _harness.Saves);
        Assert.False(result.RestartRequired);

        await _harness.Service.RevertAsync("admin", TestContext.Current.CancellationToken);
        Assert.False(_harness.SavedTrickplay.EnableHwAcceleration);
    }

    /// <summary>An optional option can be turned on, but an untested one can't.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task OptionalCanBeTurnedOn()
    {
        var result = await ApplyAsync(("Trickplay:EnableKeyFrameOnlyExtraction", true));

        Assert.Equal(ApplyOutcome.Applied, result.Outcome);
        Assert.True(_harness.SavedTrickplay.EnableKeyFrameOnlyExtraction);
    }

    /// <summary>A history written before labels were recorded comes back with them filled in.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task OldHistoryGetsLabels()
    {
        await File.WriteAllTextAsync(
            _harness.HistoryPath,
            """[{"timeUtc":"2026-10-02T07:38:05Z","user":"admin","kind":"Apply","changes":[{"setting":"EnableTonemapping","oldValue":"false","newValue":"true"},{"setting":"QsvDevice","oldValue":"","newValue":"/dev/dri/renderD128"}]}]""",
            TestContext.Current.CancellationToken);

        var history = await _harness.Service.HistoryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Enable Tone mapping", null], history[0].Changes.Select(c => c.Label));
    }

    /// <summary>Revert restores the last apply, leaves settings changed since, and then has nothing left.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task RevertRestoresUnlessChangedSince()
    {
        var ct = TestContext.Current.CancellationToken;
        await ApplyAsync(("HardwareDecodingCodecs:hevc", true), ("AllowAv1Encoding", false));
        _harness.Saved.AllowAv1Encoding = true;

        var revert = await _harness.Service.RevertAsync("admin", ct);

        Assert.Equal(ApplyOutcome.Applied, revert.Outcome);
        Assert.Equal(["h264"], _harness.Saved.HardwareDecodingCodecs);
        Assert.Contains("AllowAv1Encoding", revert.Reason, StringComparison.Ordinal);
        Assert.Equal(ApplyOutcome.NothingToRevert, (await _harness.Service.RevertAsync("admin", ct)).Outcome);

        var history = await _harness.Service.HistoryAsync(ct);
        Assert.Equal([HistoryKind.Apply, HistoryKind.Revert], history.Select(e => e.Kind));
        Assert.NotNull(history[0].RevertedUtc);
        Assert.Equal("admin", history[0].User);
        Assert.Equal("HEVC", history[0].Changes[0].Label);
    }

    /// <inheritdoc/>
    public void Dispose() => _harness.Dispose();

    /// <summary>Applies changes as the admin user.</summary>
    /// <param name="changes">Setting keys and values.</param>
    /// <returns>The result.</returns>
    private Task<ApplyResult> ApplyAsync(params (string Setting, bool Value)[] changes) =>
        _harness.Service.ApplyAsync([.. changes.Select(c => new SettingChange(c.Setting, c.Value))], "admin", TestContext.Current.CancellationToken);
}

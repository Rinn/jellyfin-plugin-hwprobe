using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.HwProbe.Core.Storage;

/// <summary>Computes the cache key for a host + ffmpeg combination.</summary>
public static class Fingerprint
{
    private const string Unknown = "unknown";

    /// <summary>Hashes the inputs as SHA-256.</summary>
    /// <param name="inputs">The fingerprint components.</param>
    /// <returns><c>sha256:</c> followed by lowercase hex.</returns>
    public static string Compute(FingerprintInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        // One component per line; lists and maps are sorted so enumeration order can't change the key.
        var text = new StringBuilder();
        Append(text, "ffmpeg.path", inputs.FfmpegPath);
        Append(text, "ffmpeg.version", inputs.FfmpegVersionLine);
        Append(text, "ffmpeg.hwaccels", Join(inputs.Hwaccels));
        Append(text, "devices", Join(inputs.Devices));
        var identities = inputs.DeviceIdentities?.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value ?? Unknown}");
        Append(text, "devices.identity", identities is null ? null : string.Join(';', identities));
        Append(text, "kernel.release", inputs.KernelRelease);
        Append(text, "os.platform", inputs.OsPlatform);
        Append(text, "os.version", inputs.OsVersion);
        if (inputs.ToolBuild is not null)
        {
            Append(text, "tool.build", inputs.ToolBuild);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return "sha256:" + Convert.ToHexStringLower(hash);
    }

    /// <summary>Appends one labelled component.</summary>
    /// <param name="text">The buffer.</param>
    /// <param name="label">Component label.</param>
    /// <param name="value">Component value; null becomes <c>unknown</c>.</param>
    private static void Append(StringBuilder text, string label, string? value) =>
        text.Append(label).Append('=').Append(value ?? Unknown).Append('\n');

    /// <summary>Joins a list in ordinal order.</summary>
    /// <param name="values">The list, or null.</param>
    /// <returns>The joined list, or null when the list is null.</returns>
    private static string? Join(IReadOnlyList<string>? values) =>
        values is null ? null : string.Join(',', values.Order(StringComparer.Ordinal));
}

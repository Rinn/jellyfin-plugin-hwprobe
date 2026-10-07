using Jellyfin.Plugin.HwProbe.Core.Storage;
using Jellyfin.Plugin.HwProbe.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Storage;

/// <summary>Replacing files whole in <see cref="AtomicFile"/>.</summary>
[Trait("Category", "Unit")]
[Trait("Category", "Platform")]
public sealed class AtomicFileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("hwprobe-atomic-").FullName;

    /// <summary>A write replaces the file and leaves no temporary file behind.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ReplacesTheFile()
    {
        var path = Path.Combine(_directory, "nested", "report.json");

        await AtomicFile.WriteAllTextAsync(path, "first", TestContext.Current.CancellationToken);
        await AtomicFile.WriteAllTextAsync(path, "second", TestContext.Current.CancellationToken);

        Assert.Equal("second", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    /// <summary>A write waits for a reader that has the file open, which on Windows blocks replacing it.</summary>
    /// <returns>A task representing the test.</returns>
    [Fact(Skip = "Requires Windows: other systems replace an open file.", SkipUnless = nameof(TestEnvironment.IsWindows), SkipType = typeof(TestEnvironment))]
    public async Task WaitsForAReader()
    {
        var path = Path.Combine(_directory, "report.json");
        await File.WriteAllTextAsync(path, "first", TestContext.Current.CancellationToken);
        Task write;
        await using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            write = AtomicFile.WriteAllTextAsync(path, "second", TestContext.Current.CancellationToken);
            await Task.Delay(150, TestContext.Current.CancellationToken);
            Assert.False(write.IsCompleted);
        }

        await write;
        Assert.Equal("second", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}

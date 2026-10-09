using Jellyfin.Plugin.HwProbe.Core.Devices;
using Jellyfin.Plugin.HwProbe.Core.Resources;
using Jellyfin.Plugin.HwProbe.Core.Tests.Devices;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.Core.Tests.Resources;

/// <summary>Free memory from <c>/proc/meminfo</c> and the server's cgroup, over scripted files.</summary>
[Trait("Category", "Unit")]
public sealed class MemoryHeadroomTests
{
    private const long GiB = 1024L * 1024 * 1024;
    private const string Meminfo = "MemTotal:       16000000 kB\nMemFree:         1000000 kB\nMemAvailable:   12000000 kB\n";

    /// <summary>A container's cgroup v2 limit caps what's free, its inactive page cache counting as free, and its limit is the total.</summary>
    [Fact]
    public void CgroupV2LimitCapsWhatsFree()
    {
        var host = Linux("0::/\n");
        host.Files["/sys/fs/cgroup/memory.max"] = (8 * GiB).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n";
        host.Files["/sys/fs/cgroup/memory.current"] = (7 * GiB).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n";
        host.Files["/sys/fs/cgroup/memory.stat"] = $"anon 1000\nshmem 6953291776\ninactive_file {GiB}\n";

        var snapshot = new MemoryHeadroom(host).Read();
        Assert.NotNull(snapshot);
        Assert.Equal((2 * GiB, 8 * GiB), (snapshot.Available, snapshot.Total));
    }

    /// <summary>With no cgroup limit (<c>max</c>), the host's available memory counts.</summary>
    [Fact]
    public void UnlimitedCgroupUsesTheHost()
    {
        var host = Linux("0::/system.slice/jellyfin.service\n");
        host.Files["/sys/fs/cgroup/system.slice/jellyfin.service/memory.max"] = "max\n";
        host.Files["/sys/fs/cgroup/system.slice/jellyfin.service/memory.current"] = "123\n";

        var snapshot = new MemoryHeadroom(host).Read();
        Assert.NotNull(snapshot);
        Assert.Equal((12000000L * 1024, 16000000L * 1024), (snapshot.Available, snapshot.Total));
    }

    /// <summary>A cgroup v1 memory limit is read from the memory controller's directory.</summary>
    [Fact]
    public void CgroupV1LimitCapsWhatsFree()
    {
        var host = Linux("12:memory:/docker/abc\n0::/\n");
        host.Files["/sys/fs/cgroup/memory/docker/abc/memory.limit_in_bytes"] = (4 * GiB).ToString(System.Globalization.CultureInfo.InvariantCulture);
        host.Files["/sys/fs/cgroup/memory/docker/abc/memory.usage_in_bytes"] = (3 * GiB).ToString(System.Globalization.CultureInfo.InvariantCulture);
        host.Files["/sys/fs/cgroup/memory/docker/abc/memory.stat"] = "total_inactive_file 0\n";

        var snapshot = new MemoryHeadroom(host).Read();
        Assert.NotNull(snapshot);
        Assert.Equal((GiB, 4 * GiB), (snapshot.Available, snapshot.Total));
    }

    /// <summary>Docker on cgroup v1 shows the container its own memory cgroup at the root, not under the path its membership names.</summary>
    [Fact]
    public void CgroupV1ContainerSeesItsOwnRoot()
    {
        var host = Linux("6:memory:/docker/2b6a\n");
        host.Files["/sys/fs/cgroup/memory/memory.limit_in_bytes"] = (2 * GiB).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n";
        host.Files["/sys/fs/cgroup/memory/memory.usage_in_bytes"] = "142606336\n";
        host.Files["/sys/fs/cgroup/memory/memory.stat"] = "inactive_file 32026624\ntotal_inactive_file 32026624\n";

        var snapshot = new MemoryHeadroom(host).Read();

        Assert.NotNull(snapshot);
        Assert.Equal(((2 * GiB) - (142606336 - 32026624), 2 * GiB), (snapshot.Available, snapshot.Total));
    }

    /// <summary>Off Linux, or without <c>/proc/meminfo</c>, nothing is known.</summary>
    [Fact]
    public void UnknownElsewhere()
    {
        Assert.Null(new MemoryHeadroom(new FakeHostPlatform(HostOs.Windows)).Read());
        Assert.Null(new MemoryHeadroom(new FakeHostPlatform(HostOs.Linux)).Read());
    }

    /// <summary>The server's CPU time comes from its cgroup when it can be read, v2 then v1, so other containers' load doesn't count.</summary>
    [Fact]
    public void ServerCpuReadsItsCgroup()
    {
        var v2 = new FakeHostPlatform(HostOs.Linux);
        v2.Files["/proc/self/cgroup"] = "0::/\n";
        v2.Files["/sys/fs/cgroup/cpu.stat"] = "usage_usec 12500000\nuser_usec 10000000\n";
        v2.Files["/proc/stat"] = "cpu  1000 100 400 9000 500 0 0 0 0 0\n";
        var v1 = new FakeHostPlatform(HostOs.Linux);
        v1.Files["/proc/self/cgroup"] = "8:cpuacct:/docker/2b6a\n";
        v1.Files["/sys/fs/cgroup/cpuacct/cpuacct.usage"] = "3000000000\n";

        Assert.Equal(12.5, Assert.NotNull(new ServerCpu(v2).BusySeconds()), 6);
        Assert.Equal(3, Assert.NotNull(new ServerCpu(v1).BusySeconds()), 6);
    }

    /// <summary>Without a cgroup, the host's busy CPU time is everything in the cpu line but idle and iowait, at 100 ticks a second.</summary>
    [Fact]
    public void HostBusyCpuExcludesIdle()
    {
        var host = new FakeHostPlatform(HostOs.Linux);
        host.Files["/proc/stat"] = "cpu  1000 100 400 9000 500 0 0 0 0 0\ncpu0 1 2 3 4 5 6 7 8 9 10\n";

        Assert.Equal(15, Assert.NotNull(new ServerCpu(host).BusySeconds()), 6);
        Assert.Null(new ServerCpu(new FakeHostPlatform(HostOs.MacOS)).BusySeconds());
    }

    /// <summary>Creates a Linux host with the meminfo above and a cgroup membership.</summary>
    /// <param name="cgroup">The text of <c>/proc/self/cgroup</c>.</param>
    /// <returns>The host.</returns>
    private static FakeHostPlatform Linux(string cgroup)
    {
        var host = new FakeHostPlatform(HostOs.Linux);
        host.Files["/proc/meminfo"] = Meminfo;
        host.Files["/proc/self/cgroup"] = cgroup;
        return host;
    }
}

using GamesHud.Api.Metrics.Services;

namespace GamesHud.Api.Tests;

public sealed class HostMetricsFileSystemTests
{
    [Fact]
    public void UnixDriveLookupPreservesConfiguredPath()
    {
        const string configuredPath = "/mnt/gameshud/servers";

        Assert.Equal(configuredPath, HostMetricsFileSystem.ResolveDriveInfoPath(configuredPath, isWindows: false));
    }
}

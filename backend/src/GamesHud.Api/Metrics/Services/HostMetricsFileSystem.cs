namespace GamesHud.Api.Metrics.Services;

public interface IHostMetricsFileSystem
{
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);

    DriveInfo GetDriveInfo(string path);
}

public sealed class HostMetricsFileSystem : IHostMetricsFileSystem
{
    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
    {
        return File.ReadAllTextAsync(path, cancellationToken);
    }

    public DriveInfo GetDriveInfo(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return new DriveInfo(ResolveDriveInfoPath(fullPath, OperatingSystem.IsWindows()));
    }

    internal static string ResolveDriveInfoPath(string fullPath, bool isWindows)
    {
        if (!isWindows) return fullPath;
        var root = Path.GetPathRoot(fullPath);
        return string.IsNullOrWhiteSpace(root) ? fullPath : root;
    }
}

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
        var root = Path.GetPathRoot(fullPath);

        return new DriveInfo(string.IsNullOrWhiteSpace(root) ? fullPath : root);
    }
}

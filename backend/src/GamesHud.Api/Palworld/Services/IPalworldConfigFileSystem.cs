namespace GamesHud.Api.Palworld.Services;

public interface IPalworldConfigFileSystem
{
    bool DirectoryExists(string path);

    IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);

    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);

    Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken);

    void Copy(string sourceFileName, string destFileName, bool overwrite);

    void Move(string sourceFileName, string destFileName, bool overwrite);

    void DeleteIfExists(string path);
}

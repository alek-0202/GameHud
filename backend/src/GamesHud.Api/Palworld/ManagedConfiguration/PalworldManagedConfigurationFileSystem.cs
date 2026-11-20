namespace GamesHud.Api.Palworld.ManagedConfiguration;

public interface IPalworldManagedConfigurationFileSystem
{
    FileAttributes? GetAttributesOrNull(string path);
    void CreateDirectory(string path);
    IReadOnlyCollection<string> EnumerateEntries(string directory, string searchPattern);
    Stream OpenRead(string path);
    Stream CreateNew(string path);
    void FlushToDisk(Stream stream);
    void MoveNoReplace(string source, string destination);
    void Delete(string path);
}

public sealed class SystemPalworldManagedConfigurationFileSystem : IPalworldManagedConfigurationFileSystem
{
    public FileAttributes? GetAttributesOrNull(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public IReadOnlyCollection<string> EnumerateEntries(string directory, string searchPattern) =>
        Directory.GetFileSystemEntries(directory, searchPattern, SearchOption.TopDirectoryOnly);

    public Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
        bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);

    public Stream CreateNew(string path) => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
        bufferSize: 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);

    public void FlushToDisk(Stream stream)
    {
        stream.Flush();
        if (stream is FileStream fileStream) fileStream.Flush(flushToDisk: true);
    }

    public void MoveNoReplace(string source, string destination) => File.Move(source, destination, overwrite: false);
    public void Delete(string path) => File.Delete(path);
}

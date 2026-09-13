using System.IO.Compression;
namespace PRomda.FileShare;
public static class ArchiveHelper
{
    public static async Task AddFileAsync(string filePath, string archivePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
        var name = Path.GetFileName(filePath);
        archive.GetEntry(name)?.Delete();
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        await using var source = File.OpenRead(filePath);
        await using var target = entry.Open();
        await source.CopyToAsync(target, 65536, ct);
    }
}

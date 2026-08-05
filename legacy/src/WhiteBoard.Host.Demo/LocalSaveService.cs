using System.IO;
using WhiteBoard.Contracts.Services;

namespace WhiteBoard.Host.Demo;

/// <summary>
/// 文件系统实现的 ISaveService：每个 documentId 对应 &lt;BaseDir&gt;/&lt;documentId&gt; 路径下的文件。
/// 用于 Demo 本地保存；生产实现应替换为原子写（tmp → rename）。
/// </summary>
public sealed class LocalSaveService : ISaveService
{
    private readonly string _baseDir;

    public LocalSaveService(string? baseDir = null)
    {
        _baseDir = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WhiteBoard", "Saves");
        Directory.CreateDirectory(_baseDir);
    }

    public async Task SaveAsync(string documentId, Stream data, CancellationToken ct = default)
    {
        var path = GetPath(documentId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // 原子写：先写 tmp 文件，再 rename，避免部分写入损坏
        var tmp = path + ".tmp";
        await using var fs = File.Open(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
        await data.CopyToAsync(fs, ct);
        await fs.FlushAsync(ct);
        fs.Close();

        File.Move(tmp, path, overwrite: true);
    }

    public Task<Stream?> LoadAsync(string documentId, CancellationToken ct = default)
    {
        var path = GetPath(documentId);
        if (!File.Exists(path))
            return Task.FromResult<Stream?>(null);

        return Task.FromResult<Stream?>(File.OpenRead(path));
    }

    public Task<bool> ExistsAsync(string documentId, CancellationToken ct = default)
        => Task.FromResult(File.Exists(GetPath(documentId)));

    private string GetPath(string documentId)
        => Path.Combine(_baseDir, SanitizeId(documentId));

    private static string SanitizeId(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(id.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
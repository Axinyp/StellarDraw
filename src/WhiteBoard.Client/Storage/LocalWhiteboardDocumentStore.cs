using System.IO;
using Microsoft.Win32;
using WhiteBoard.Core.Services;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.Client.Storage;

/// <summary>Client 白板文档的本地文件边界；只返回可面向用户展示的通用结果。</summary>
public interface ILocalWhiteboardDocumentStore
{
    Task<DocumentOperationResult> SaveAsAsync(WhiteboardViewModel board, CancellationToken ct = default);
    Task<DocumentOperationResult> OpenAsync(WhiteboardViewModel board, CancellationToken ct = default);
}

/// <summary>本地文件操作的非敏感结果。</summary>
public sealed record DocumentOperationResult(bool Succeeded, string Message)
{
    public static DocumentOperationResult Success(string message) => new(true, message);
    public static DocumentOperationResult Failure(string message) => new(false, message);
}

/// <summary>
/// 保存和打开 <c>.stellarboard</c> 文档。保存先写同目录临时文件再替换目标，打开先完整解析再替换 Session。
/// </summary>
public sealed class LocalWhiteboardDocumentStore : ILocalWhiteboardDocumentStore
{
    private const long MaxDocumentBytes = 250L * 1024 * 1024;
    private const string Filter = "StellarDraw 白板文档|*.stellarboard|所有文件|*.*";

    public async Task<DocumentOperationResult> SaveAsAsync(WhiteboardViewModel board, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(board);
        var dialog = new SaveFileDialog
        {
            Title = "另存为白板文档",
            Filter = Filter,
            DefaultExt = ".stellarboard",
            AddExtension = true,
        };
        if (dialog.ShowDialog() != true)
            return DocumentOperationResult.Failure("已取消保存。");

        try
        {
            var document = await board.CaptureDocumentAsync(ct);
            var data = WhiteboardDocumentSerializer.Serialize(document);
            ct.ThrowIfCancellationRequested();
            await WriteAtomicallyAsync(dialog.FileName, data, ct);
            return DocumentOperationResult.Success("白板文档已保存。");
        }
        catch (OperationCanceledException)
        {
            return DocumentOperationResult.Failure("已取消保存。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            System.Diagnostics.Debug.WriteLine($"[Client] Failed to save whiteboard document: {ex.GetType().Name}");
            return DocumentOperationResult.Failure("无法保存白板文档。请检查文件是否可写后重试。");
        }
    }

    public async Task<DocumentOperationResult> OpenAsync(WhiteboardViewModel board, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(board);
        var dialog = new OpenFileDialog
        {
            Title = "打开白板文档",
            Filter = Filter,
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true)
            return DocumentOperationResult.Failure("已取消打开。");

        try
        {
            var file = new FileInfo(dialog.FileName);
            if (!file.Exists || file.Length is <= 0 or > MaxDocumentBytes)
                return DocumentOperationResult.Failure("白板文档大小无效。");

            var data = await File.ReadAllBytesAsync(file.FullName, ct);
            var document = WhiteboardDocumentSerializer.Deserialize(data);
            ct.ThrowIfCancellationRequested();
            await board.ReplaceDocumentAsync(document, ct);
            return DocumentOperationResult.Success("白板文档已打开。");
        }
        catch (OperationCanceledException)
        {
            return DocumentOperationResult.Failure("已取消打开。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            System.Diagnostics.Debug.WriteLine($"[Client] Failed to open whiteboard document: {ex.GetType().Name}");
            return DocumentOperationResult.Failure("无法打开白板文档。该文件可能已损坏或不受支持。");
        }
    }

    private static async Task WriteAtomicallyAsync(string destinationPath, byte[] data, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new IOException("目标目录不存在。");

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true))
            {
                await stream.WriteAsync(data, ct);
                await stream.FlushAsync(ct);
            }
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}

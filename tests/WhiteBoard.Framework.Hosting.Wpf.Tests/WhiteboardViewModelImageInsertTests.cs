using System.IO;
using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Windows.Threading;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;
using WhiteBoard.Core.Services;
using WhiteBoard.UI.ViewModels;

namespace WhiteBoard.Framework.Hosting.Wpf.Tests;

public sealed class WhiteboardViewModelImageInsertTests
{
    [Fact]
    public async Task InsertImageFromBytesAsync_ValidImage_AddsObjectAndRequestsRedraw()
    {
        await Sta.RunAsync(async () =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            await using var session = new WhiteboardSession();
            await session.StartAsync();
            using var viewModel = new WhiteboardViewModel(session, dispatcher);
            var redrawn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            viewModel.RedrawRequested += () => redrawn.TrySetResult();

            var result = await viewModel.InsertImageFromBytesAsync(CreateGreenPng());

            Assert.True(result.Succeeded);
            Assert.NotNull(result.ObjectId);
            Assert.NotNull(result.AssetId);
            await redrawn.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(RawImageAssetStore.Retrieve(result.AssetId!));
            Assert.NotNull(viewModel.CurrentRenderPage?.GetObject(result.ObjectId!.Value));
            var snapshot = await session.CaptureSnapshotAsync();
            Assert.Contains(snapshot.Objects, item => item.Id == result.ObjectId && item.Kind == nameof(ImageObject));
        });
    }

    [Fact]
    public async Task InsertImageFromBytesAsync_InvalidImage_RejectsWithoutChangingSession()
    {
        await Sta.RunAsync(async () =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            await using var session = new WhiteboardSession();
            await session.StartAsync();
            using var viewModel = new WhiteboardViewModel(session, dispatcher);

            var result = await viewModel.InsertImageFromBytesAsync([1, 2, 3]);

            Assert.False(result.Succeeded);
            Assert.Equal("图片格式或内容无效。", result.Message);
            Assert.Empty((await session.CaptureSnapshotAsync()).Objects);
        });
    }

    private static byte[] CreateGreenPng()
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 1);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], 1);
        header[8] = 8;
        header[9] = 6;
        WritePngChunk(output, "IHDR"u8, header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write([0, 0, 255, 0, 255]);
        WritePngChunk(output, "IDAT"u8, compressed.ToArray());
        WritePngChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void WritePngChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)data.Length);
        output.Write(buffer);
        output.Write(type);
        output.Write(data);

        var crcInput = new byte[type.Length + data.Length];
        type.CopyTo(crcInput);
        data.CopyTo(crcInput.AsSpan(type.Length));
        BinaryPrimitives.WriteUInt32BigEndian(buffer, Crc32.HashToUInt32(crcInput));
        output.Write(buffer);
    }
}

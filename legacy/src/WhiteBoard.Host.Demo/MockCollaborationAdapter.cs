using System.Runtime.CompilerServices;
using WhiteBoard.Contracts.Models;
using WhiteBoard.Contracts.Services;

namespace WhiteBoard.Host.Demo;

/// <summary>
/// 模拟远端协同事件：每 2 秒发送一个 ObjectTransformedEvent，用于验证
/// Session Executor 能正确将远端事件序列化到本地命令队列。
/// </summary>
public sealed class MockCollaborationAdapter : ICollaborationAdapter, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private static long _seqCounter;

    public bool IsActive => true;

    public ValueTask OnLocalEventAsync(WhiteboardEvent evt)
    {
        System.Diagnostics.Debug.WriteLine($"[MockCollab] Local → {evt.GetType().Name} obj={evt.ObjectId}");
        return default;
    }

    public async IAsyncEnumerable<WhiteboardEvent> RemoteEventsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);

        while (!linked.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), linked.Token);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            var seq = Interlocked.Increment(ref _seqCounter);
            yield return new ObjectTransformedEvent(
                EventId: Guid.NewGuid().ToString("N"),
                PageId: "demo-page",
                ObjectId: Guid.NewGuid().ToString("N"),
                ClientId: "remote-peer-1",
                OccurredAtUtc: DateTimeOffset.UtcNow,
                Matrix6: [1f, 0f, 0f, 1f, seq * 5f, 0f]);
        }
    }

    public void Dispose() => _cts.Cancel();
}
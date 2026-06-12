using System.Threading.Channels;
using WhiteBoard.Contracts.Models;
using WhiteBoard.Contracts.Services;
using WhiteBoard.Core.Commands;
using WhiteBoard.Core.Domain.Objects;
using WhiteBoard.Core.Services;

namespace WhiteBoard.Core.Domain;

/// <summary>
/// 白板核心会话：管理页面集合、命令历史、协同适配器和笔迹预测器。
///
/// 线程安全模型：
///   所有状态变更（AddStroke、EraseAt、Undo/Redo）必须通过 <see cref="PostOperationAsync"/> 入队，
///   由单一执行器（RunExecutorAsync）串行消费，保证状态一致性。
///   UI 线程只负责入队，不直接读写内部集合。
///
/// 生命周期：
///   <see cref="StartAsync"/> 启动执行器与远端监听循环；
///   <see cref="StopAsync"/> / <see cref="DisposeAsync"/> 安全取消并等待任务结束。
/// </summary>
public sealed class WhiteboardSession : IAsyncDisposable
{
    private readonly ICollaborationAdapter _collab;
    private readonly UndoRedoStack _history;
    private readonly PageManager _pages;
    private readonly OcrService _ocr;
    private readonly SelectionModel _selection;
    private readonly Channel<Func<ValueTask>> _ops;
    private IStrokePredictor? _predictor;
    private CancellationTokenSource? _cts;
    private Task? _executorTask;
    private Task? _remoteTask;

    public WhiteboardSession(ICollaborationAdapter? collab = null)
    {
        _collab    = collab ?? NullCollaborationAdapter.Instance;
        _history   = new UndoRedoStack(maxDepth: 200);
        _pages     = new PageManager();
        _ocr       = new OcrService();
        _selection = new SelectionModel();
        _ops       = Channel.CreateUnbounded<Func<ValueTask>>(
            new UnboundedChannelOptions { SingleReader = true });

        _pages.EnsureAtLeastOnePage();
    }

    public PageManager    Pages     => _pages;
    public OcrService     Ocr       => _ocr;
    public UndoRedoStack  History   => _history;
    public PageModel      Current   => _pages.Current;
    public SelectionModel Selection => _selection;
    public IStrokePredictor? Predictor => _predictor;

    public void RegisterPredictor(IStrokePredictor predictor) => _predictor = predictor;

    public async Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _executorTask = RunExecutorAsync(_cts.Token);
        _remoteTask = StartRemoteListenAsync(_cts.Token);
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync();
        _ops.Writer.TryComplete();
        try { if (_executorTask != null) await _executorTask; } catch (OperationCanceledException) { }
        try { if (_remoteTask != null) await _remoteTask; } catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 所有 Session 状态变更必须通过此方法序列化，禁止在 UI/后台线程直接修改 Session 状态。
    /// </summary>
    public ValueTask PostOperationAsync(Func<ValueTask> op, CancellationToken ct = default)
        => _ops.Writer.WriteAsync(op, ct);

    public ValueTask PostOperationAsync(Action op, CancellationToken ct = default)
        => _ops.Writer.WriteAsync(() => { op(); return ValueTask.CompletedTask; }, ct);

    public async Task AddStrokeAsync(StrokeObject stroke, CancellationToken ct = default)
    {
        var cmd        = new AddObjectCommand(stroke, _pages.Current);
        var strokeData = PageSerializer.SerializeObject(stroke);
        var local      = new StrokeAddedEvent(
            Guid.NewGuid().ToString("N"),
            _pages.Current.Id,
            stroke.Id.ToString(),
            "local",
            DateTimeOffset.UtcNow,
            strokeData);

        // TCS 让调用方等到 Executor 真正执行完，保证 RequestRedraw 触发时页面已有新笔画
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await PostOperationAsync(async () =>
        {
            try
            {
                _history.Execute(cmd);
                await _collab.OnLocalEventAsync(local);
            }
            finally
            {
                done.TrySetResult();
            }
        }, ct);
        await done.Task.WaitAsync(ct);
    }

    /// <summary>
    /// 在文档坐标 (x, y) 附近执行橡皮擦：命中的所有对象删除并广播 <see cref="ObjectDeletedEvent"/>。
    /// 命中检测在调用线程完成（命中列表快照），实际删除与广播在执行器线程串行执行。
    /// </summary>
    public async Task EraseAtAsync(float x, float y, CancellationToken ct = default)
    {
        var page = _pages.Current;
        var hits = page.HitTest(x, y, tolerance: 8f);
        if (hits.Count == 0) return;

        // 提前生成命令与事件，避免执行器线程重新 HitTest
        var cmds = hits.Select(obj => new DeleteObjectCommand(obj, page)).ToList();
        var evts = hits.Select(obj => new ObjectDeletedEvent(
            Guid.NewGuid().ToString("N"),
            page.Id,
            obj.Id.ToString(),
            "local",
            DateTimeOffset.UtcNow)).ToList();

        await PostOperationAsync(async () =>
        {
            foreach (var cmd in cmds)
                _history.Execute(cmd);
            foreach (var evt in evts)
                await _collab.OnLocalEventAsync(evt);
        }, ct);
    }

    /// <summary>
    /// 在执行器线程内构建当前页面的不可变快照，供 AI 工具层安全读取。
    /// 通过 PostOperationAsync 保证快照在 Session 执行器线程内构建，消除竞争。
    /// </summary>
    public async Task<PageSnapshot> CaptureSnapshotAsync(CancellationToken ct = default)
    {
        PageSnapshot? snapshot = null;
        var done = new TaskCompletionSource<PageSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);

        await PostOperationAsync(() =>
        {
            var page    = _pages.Current;
            var objects = page.GetAllObjects();
            var summaries = objects.Select(obj => new ObjectSummary(
                Id:       obj.Id,
                Kind:     obj.GetType().Name,
                Alias:    null,
                X:        obj.BoundsX,
                Y:        obj.BoundsY,
                Width:    obj.BoundsWidth,
                Height:   obj.BoundsHeight,
                ZIndex:   obj.ZIndex,
                IsLocked: obj.IsLocked,
                OwnerId:  obj.OwnerId
            )).ToList();

            snapshot = new PageSnapshot
            {
                PageId  = page.Id,
                Version = page.Version,
                Width   = 1920f,
                Height  = 1080f,
                Objects = summaries,
            };
            done.TrySetResult(snapshot);
            return ValueTask.CompletedTask;
        }, ct);

        return await done.Task.WaitAsync(ct);
    }

    public async Task AddShapeAsync(ShapeObject shape, string? operationId = null,
        string? groupId = null, CancellationToken ct = default)
    {
        var cmd  = new AddShapeCommand(shape, _pages.Current);
        var data = PageSerializer.SerializeObject(shape);
        var evt  = new ObjectAddedEvent(
            Guid.NewGuid().ToString("N"), _pages.Current.Id, shape.Id.ToString(), "local",
            DateTimeOffset.UtcNow, data,
            operationId ?? Guid.NewGuid().ToString("N"), groupId);

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await PostOperationAsync(async () =>
        {
            try   { _history.Execute(cmd); await _collab.OnLocalEventAsync(evt); }
            finally { done.TrySetResult(); }
        }, ct);
        await done.Task.WaitAsync(ct);
    }

    public async Task AddTextObjectAsync(TextObject text, string? operationId = null,
        string? groupId = null, CancellationToken ct = default)
    {
        var cmd  = new AddTextCommand(text, _pages.Current);
        var data = PageSerializer.SerializeObject(text);
        var evt  = new ObjectAddedEvent(
            Guid.NewGuid().ToString("N"), _pages.Current.Id, text.Id.ToString(), "local",
            DateTimeOffset.UtcNow, data,
            operationId ?? Guid.NewGuid().ToString("N"), groupId);

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await PostOperationAsync(async () =>
        {
            try   { _history.Execute(cmd); await _collab.OnLocalEventAsync(evt); }
            finally { done.TrySetResult(); }
        }, ct);
        await done.Task.WaitAsync(ct);
    }

    public async Task ClearCurrentPageAsync(string? operationId = null, CancellationToken ct = default)
    {
        var page = _pages.Current;
        var cmd  = new ClearPageCommand(page);
        var evt  = new PageClearedEvent(
            Guid.NewGuid().ToString("N"), page.Id, "", "local",
            DateTimeOffset.UtcNow, operationId ?? Guid.NewGuid().ToString("N"));

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await PostOperationAsync(async () =>
        {
            try   { _history.Execute(cmd); await _collab.OnLocalEventAsync(evt); }
            finally { done.TrySetResult(); }
        }, ct);
        await done.Task.WaitAsync(ct);
    }

    public Task UndoAsync(CancellationToken ct = default)
        => PostOperationAsync(() => { _history.Undo(); return ValueTask.CompletedTask; }, ct).AsTask();

    public Task RedoAsync(CancellationToken ct = default)
        => PostOperationAsync(() => { _history.Redo(); return ValueTask.CompletedTask; }, ct).AsTask();

    public async Task DeleteObjectsAsync(IReadOnlyList<Guid> objectIds, string? operationId = null,
        CancellationToken ct = default)
    {
        var page = _pages.Current;
        var objs = objectIds.Select(id => page.GetObject(id)).OfType<WhiteboardObject>().ToList();
        if (objs.Count == 0) return;

        var cmds = objs.Select(o => new DeleteObjectCommand(o, page)).ToList();
        var evts = objs.Select(o => new ObjectDeletedEvent(
            Guid.NewGuid().ToString("N"), page.Id, o.Id.ToString(), "local",
            DateTimeOffset.UtcNow)).ToList();

        await PostOperationAsync(async () =>
        {
            foreach (var cmd in cmds) _history.Execute(cmd);
            foreach (var evt in evts) await _collab.OnLocalEventAsync(evt);
        }, ct);
    }

    public async Task MoveObjectsAsync(IReadOnlyList<Guid> objectIds, float dx, float dy,
        string? operationId = null, CancellationToken ct = default)
    {
        var page = _pages.Current;
        var objs = objectIds.Select(id => page.GetObject(id)).OfType<WhiteboardObject>().ToList();
        if (objs.Count == 0) return;

        var cmd = new TransformCommand(objs, page, dx, dy);
        var evt = new ObjectsTransformedEvent(
            Guid.NewGuid().ToString("N"), page.Id, "", "local", DateTimeOffset.UtcNow,
            objectIds.Select(id => id.ToString()).ToList(),
            [1f, 0f, 0f, 1f, dx, dy],
            operationId ?? Guid.NewGuid().ToString("N"));

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await PostOperationAsync(async () =>
        {
            try   { _history.Execute(cmd); await _collab.OnLocalEventAsync(evt); }
            finally { done.TrySetResult(); }
        }, ct);
        await done.Task.WaitAsync(ct);
    }

    public Task SelectObjectsInRegionAsync(float x, float y, float w, float h,
        CancellationToken ct = default)
        => PostOperationAsync(() =>
        {
            var hits = _pages.Current.LassoSelect(x, y, x + w, y + h);
            _selection.SelectAll(hits.Select(o => o.Id));
            return ValueTask.CompletedTask;
        }, ct).AsTask();

    private async Task RunExecutorAsync(CancellationToken ct)
    {
        await foreach (var op in _ops.Reader.ReadAllAsync(ct))
        {
            try { await op(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 单次操作失败不中断执行器；实际项目中接入 IHostLogger
                System.Diagnostics.Debug.WriteLine($"[Session] Op failed: {ex.Message}");
            }
        }
    }

    private async Task StartRemoteListenAsync(CancellationToken ct)
    {
        await foreach (var evt in _collab.RemoteEventsAsync(ct))
        {
            if (ct.IsCancellationRequested) break;
            await PostOperationAsync(() => ApplyRemoteEvent(evt), ct);
        }
    }

    private void ApplyRemoteEvent(WhiteboardEvent evt)
    {
        // 远端事件直接操作 PageModel，不经过本地 _history，
        // 避免远端笔画被本地 Undo/Redo 回滚（状态发散）。
        switch (evt)
        {
            case StrokeAddedEvent added:
            {
                if (added.StrokeData.Length == 0) break;
                var page = _pages.GetPage(added.PageId);
                if (page is null) break;
                var obj = PageSerializer.DeserializeObject(added.StrokeData);
                if (obj is null) break;
                if (page.GetObject(obj.Id) is not null) break;  // dedup：已存在则跳过
                page.AddObject(obj);
                break;
            }
            case ObjectAddedEvent added:
            {
                if (added.ObjectData.Length == 0) break;
                var page = _pages.GetPage(added.PageId);
                if (page is null) break;
                var obj = PageSerializer.DeserializeObject(added.ObjectData);
                if (obj is null) break;
                if (page.GetObject(obj.Id) is not null) break;  // dedup
                page.AddObject(obj);
                break;
            }
            case ObjectDeletedEvent del:
            {
                var page = _pages.GetPage(del.PageId);
                if (page is null) break;
                if (!Guid.TryParse(del.ObjectId, out var guid)) break;
                var obj = page.GetObject(guid);
                if (obj is null) break;
                page.RemoveObject(obj);
                break;
            }
            case ObjectTransformedEvent xfm:
            {
                var page = _pages.GetPage(xfm.PageId);
                if (page is null || xfm.Matrix6.Length < 6) break;
                if (!Guid.TryParse(xfm.ObjectId, out var guid)) break;
                var obj = page.GetObject(guid);
                obj?.SetTransform(xfm.Matrix6);
                break;
            }
            case ObjectsTransformedEvent batchXfm:
            {
                var page = _pages.GetPage(batchXfm.PageId);
                if (page is null || batchXfm.Matrix6.Length < 6) break;
                foreach (var idStr in batchXfm.ObjectIds)
                {
                    if (!Guid.TryParse(idStr, out var guid)) continue;
                    page.GetObject(guid)?.SetTransform(batchXfm.Matrix6);
                }
                break;
            }
            case PageClearedEvent cleared:
            {
                var page = _pages.GetPage(cleared.PageId);
                page?.Clear();
                break;
            }
            case PageAddedEvent:
                _pages.EnsureAtLeastOnePage();
                break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }
}

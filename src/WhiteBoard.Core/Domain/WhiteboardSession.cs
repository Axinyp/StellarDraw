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
///   所有状态变更均由单一执行器（RunExecutorAsync）串行消费。
///   对外只暴露受控异步操作和不可变快照，不泄露执行器拥有的页面、历史或选择对象。
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
    private readonly object _lifecycleLock = new();
    private IStrokePredictor? _predictor;
    private CancellationTokenSource? _cts;
    private Task? _executorTask;
    private Task? _remoteTask;
    private CanvasSizeSnapshot _canvasSize = new(1920f, 1080f);
    private string _documentId = Guid.NewGuid().ToString("N");
    private long _revision;

    /// <summary>状态变更提示。处理器不得读取 Session 活对象，而应调用 <see cref="CaptureUiSnapshotAsync"/>。</summary>
    public event Action<long>? StateChanged;

    public WhiteboardSession(ICollaborationAdapter? collab = null)
    {
        _collab = collab ?? NullCollaborationAdapter.Instance;
        _history = new UndoRedoStack(maxDepth: 200);
        _pages = new PageManager();
        _ocr = new OcrService();
        _selection = new SelectionModel();
        _ops = Channel.CreateUnbounded<Func<ValueTask>>(
            new UnboundedChannelOptions { SingleReader = true }
        );

        _pages.EnsureAtLeastOnePage();
    }

    public OcrService Ocr => _ocr;
    public IStrokePredictor? Predictor => _predictor;

    /// <summary>
    /// 在执行器内原子更新画布逻辑尺寸。宽和高必须均为有限正数。
    /// </summary>
    public async Task SetCanvasSizeAsync(float width, float height, CancellationToken ct = default)
    {
        ValidateCanvasSize(width, height);
        await ExecuteAsync(() =>
        {
            _canvasSize = new CanvasSizeSnapshot(width, height);
            PublishStateChanged();
            return ValueTask.CompletedTask;
        }, ct);
    }

    /// <summary>在执行器内读取成对画布逻辑尺寸。</summary>
    public Task<CanvasSizeSnapshot> GetCanvasSizeAsync(CancellationToken ct = default) =>
        ExecuteAsync(() => new ValueTask<CanvasSizeSnapshot>(_canvasSize), ct);

    public async Task<PageInfo> AddPageAsync(
        string? title = null,
        string background = "white",
        CancellationToken ct = default)
    {
        var requestedTitle = title;
        var requestedBackground = background;
        return await ExecuteAsync(() =>
        {
            var page = _pages.AddPage(requestedTitle, requestedBackground);
            _pages.GoToPage(page.Id);
            _selection.Clear();
            PublishStateChanged();
            return new ValueTask<PageInfo>(ToPageInfo(page, _pages.CurrentIndex));
        }, ct);
    }

    /// <summary>按页面 ID 切换当前页面；页面不存在时返回 <see langword="false"/>。</summary>
    public Task<bool> GoToPageAsync(string pageId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        return ExecuteAsync(() =>
        {
            var changed = _pages.GoToPage(pageId);
            if (changed)
            {
                _selection.Clear();
                PublishStateChanged();
            }
            return new ValueTask<bool>(changed);
        }, ct);
    }

    /// <summary>按索引切换当前页面；页面不存在时返回 <see langword="false"/>。</summary>
    public async Task<bool> GoToPageAsync(int index, CancellationToken ct = default) =>
        (await NavigateToPageAsync(index, ct)).Changed;

    /// <summary>按索引切换当前页面，并返回执行器内实际生效页面的操作结果。</summary>
    public Task<SessionOperationResult> NavigateToPageAsync(int index, CancellationToken ct = default) =>
        ExecuteAsync(() =>
        {
            var page = _pages.Current;
            if (index < 0 || index >= _pages.AllPages.Count)
                return new ValueTask<SessionOperationResult>(CreateUnchangedResult(page));

            _pages.GoToIndex(index);
            _selection.Clear();
            page = _pages.Current;
            PublishStateChanged();
            return new ValueTask<SessionOperationResult>(CreateChangedResult(page, []));
        }, ct);

    /// <summary>在执行器内读取页面顺序与当前页信息。</summary>
    public Task<PageCollectionSnapshot> GetPagesAsync(CancellationToken ct = default) =>
        ExecuteAsync(() =>
        {
            var pages = _pages.AllPages
                .Select((page, index) => ToPageInfo(page, index))
                .ToArray();
            return new ValueTask<PageCollectionSnapshot>(
                new PageCollectionSnapshot(pages, _pages.Current.Id, _pages.CurrentIndex));
        }, ct);

    /// <summary>在执行器内捕获供 UI 消费的一致、脱离 Session 的状态快照。</summary>
    public Task<SessionUiSnapshot> CaptureUiSnapshotAsync(CancellationToken ct = default) =>
        ExecuteAsync(() =>
        {
            var currentPage = _pages.Current;
            var pages = _pages.AllPages
                .Select((page, index) => ToPageInfo(page, index))
                .ToArray();
            var pageCollection = new PageCollectionSnapshot(
                pages,
                currentPage.Id,
                _pages.CurrentIndex);
            var renderPage = new CurrentPageRenderSnapshot(
                currentPage.Id,
                currentPage.Title,
                currentPage.Background,
                currentPage.Version,
                PageSerializer.Serialize(currentPage));
            return new ValueTask<SessionUiSnapshot>(new SessionUiSnapshot(
                _revision,
                _canvasSize,
                pageCollection,
                new HistoryStateSnapshot(_history.CanUndo, _history.CanRedo),
                _selection.ToList(),
                renderPage));
        }, ct);

    /// <summary>在执行器内捕获完整可持久化文档快照。</summary>
    public Task<WhiteboardDocumentSnapshot> CaptureDocumentAsync(CancellationToken ct = default) =>
        ExecuteAsync(() =>
        {
            var images = _pages.AllPages
                .SelectMany(page => page.GetAllObjects())
                .OfType<ImageObject>()
                .ToArray();
            var assetsToPersist = images
                .Select(image => new
                {
                    SourceAssetId = image.AssetId,
                    PersistedAssetId = ToPersistedAssetId(image.AssetId),
                })
                .GroupBy(asset => asset.PersistedAssetId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();

            var pages = _pages.AllPages
                .Select(page => new WhiteboardDocumentPageSnapshot(
                    page.Id,
                    page.Title,
                    page.Background,
                    PageSerializer.Serialize(page, ToPersistedAssetId)))
                .ToArray();
            var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var asset in assetsToPersist)
            {
                var data = RawImageAssetStore.Retrieve(asset.SourceAssetId)
                    ?? throw new InvalidDataException(
                        $"图片资产 '{asset.SourceAssetId}' 不可用，无法保存文档。");
                assets.Add(asset.PersistedAssetId, (byte[])data.Clone());
            }

            return new ValueTask<WhiteboardDocumentSnapshot>(
                new WhiteboardDocumentSnapshot(
                    _documentId,
                    _canvasSize,
                    pages,
                    _pages.CurrentIndex,
                    assets));
        }, ct);

    /// <summary>
    /// 验证并原子替换完整文档。调用方必须先完成反序列化，避免无效输入导致半导入。
    /// </summary>
    public Task ReplaceDocumentAsync(WhiteboardDocumentSnapshot document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        WhiteboardDocumentSerializer.ValidateDocument(document);

        var pages = document.Pages
            .Select(page => PageSerializer.Deserialize(page.Data, page.Id, page.Title))
            .ToArray();
        for (var index = 0; index < pages.Length; index++)
            pages[index].Background = document.Pages[index].Background;
        var assets = document.Assets
            .ToDictionary(pair => pair.Key, pair => (byte[])pair.Value.Clone(), StringComparer.Ordinal);
        var canvasSize = document.CanvasSize;
        var documentId = document.DocumentId;
        if (!RawImageAssetStore.CanStoreBatch(assets))
            throw new InvalidDataException("图片资产超出进程缓存预算。");

        return ExecuteAsync(() =>
        {
            if (!RawImageAssetStore.TryStoreBatch(assets))
                throw new InvalidDataException("图片资产超出进程缓存预算。");

            _pages.ReplaceAll(pages, document.CurrentPageIndex);
            _canvasSize = canvasSize;
            _documentId = documentId;
            _history.Clear();
            _selection.Clear();
            PublishStateChanged();
            return ValueTask.CompletedTask;
        }, ct);
    }

    private static string ToPersistedAssetId(string assetId) =>
        RawImageAssetStore.TryResolveCanonicalAssetId(assetId, out var canonicalAssetId)
            ? canonicalAssetId!
            : assetId;

    private static PageInfo ToPageInfo(PageModel page, int index) =>
        new(page.Id, page.Title, page.Background, index, page.Version);

    private static void ValidateCanvasSize(float width, float height)
    {
        if (!float.IsFinite(width) || width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "画布宽度必须是有限正数。");
        if (!float.IsFinite(height) || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "画布高度必须是有限正数。");
    }

    public void RegisterPredictor(IStrokePredictor predictor) => _predictor = predictor;

    public async Task StartAsync(CancellationToken ct = default)
    {
        lock (_lifecycleLock)
        {
            if (_cts is not null)
                throw new InvalidOperationException("WhiteboardSession 已启动，不能重复启动。");

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _executorTask = RunExecutorAsync(_cts.Token);
            _remoteTask = StartRemoteListenAsync(_cts.Token);
        }
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is null)
            return;

        _ops.Writer.TryComplete();
        try
        {
            if (_executorTask != null)
                await _executorTask;
        }
        catch (OperationCanceledException) { }

        await _cts.CancelAsync();
        try
        {
            if (_remoteTask != null)
                await _remoteTask;
        }
        catch (OperationCanceledException) { }
    }

    private ValueTask EnqueueAsync(Func<ValueTask> operation, CancellationToken ct) =>
        _ops.Writer.WriteAsync(operation, ct);

    private async Task ExecuteAsync(Func<ValueTask> operation, CancellationToken ct)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await EnqueueAsync(async () =>
        {
            try
            {
                await operation();
                done.TrySetResult();
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        }, ct);
        await done.Task;
    }

    private async Task<T> ExecuteAsync<T>(Func<ValueTask<T>> operation, CancellationToken ct)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        await EnqueueAsync(async () =>
        {
            try
            {
                done.TrySetResult(await operation());
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        }, ct);
        return await done.Task;
    }

    private void DispatchLocalEvent(WhiteboardEvent evt)
    {
        try
        {
            var send = _collab.OnLocalEventAsync(evt);
            if (!send.IsCompletedSuccessfully)
                _ = ObserveLocalEventAsync(send);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Session] Local collaboration send failed: {ex.Message}");
        }
    }

    private static async Task ObserveLocalEventAsync(ValueTask send)
    {
        try
        {
            await send;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Session] Local collaboration send failed: {ex.Message}");
        }
    }

    private void PublishStateChanged()
    {
        var revision = ++_revision;
        foreach (var handler in StateChanged?.GetInvocationList().Cast<Action<long>>() ?? [])
        {
            try
            {
                handler(revision);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Session] StateChanged handler failed: {ex.Message}");
            }
        }
    }

    public Task<SessionOperationResult> AddStrokeAsync(StrokeObject stroke, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        return ExecuteAsync(async () =>
        {
            var page = _pages.Current;
            var command = new AddObjectCommand(stroke, page);
            var local = new StrokeAddedEvent(
                Guid.NewGuid().ToString("N"),
                page.Id,
                stroke.Id.ToString(),
                "local",
                DateTimeOffset.UtcNow,
                PageSerializer.SerializeObject(stroke));

            _history.Execute(command);
            PublishStateChanged();
            DispatchLocalEvent(local);
            return CreateChangedResult(page, [stroke.Id]);
        }, ct);
    }

    /// <summary>
    /// 在文档坐标 (x, y) 附近执行橡皮擦：命中的所有对象删除并广播 <see cref="ObjectDeletedEvent"/>。
    /// HitTest 在执行器线程内执行，保证与 SpatialIndex 的写操作串行，消除竞态条件。
    /// </summary>
    public Task<SessionOperationResult> EraseAtAsync(float x, float y, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            var page = _pages.Current;
            var hits = page.HitTest(x, y, tolerance: 8f).ToList();
            if (hits.Count == 0)
                return CreateUnchangedResult(page);

            foreach (var hit in hits)
                _history.Execute(new DeleteObjectCommand(hit, page));
            RemoveSelection(hits.Select(hit => hit.Id));
            PublishStateChanged();
            foreach (var hit in hits)
            {
                DispatchLocalEvent(new ObjectDeletedEvent(
                    Guid.NewGuid().ToString("N"),
                    page.Id,
                    hit.Id.ToString(),
                    "local",
                    DateTimeOffset.UtcNow));
            }

            return CreateChangedResult(page, hits.Select(hit => hit.Id).ToArray());
        }, ct);

    /// <summary>在执行器线程内构建当前页面的不可变快照，供 AI 工具层安全读取。</summary>
    public Task<PageSnapshot> CaptureSnapshotAsync(CancellationToken ct = default) =>
        ExecuteAsync(() =>
        {
            var page = _pages.Current;
            var summaries = page.GetAllObjects()
                .Select(obj => new ObjectSummary(
                    obj.Id,
                    obj.GetType().Name,
                    null,
                    obj.BoundsX,
                    obj.BoundsY,
                    obj.BoundsWidth,
                    obj.BoundsHeight,
                    obj.ZIndex,
                    obj.IsLocked,
                    obj.OwnerId))
                .ToArray();
            return new ValueTask<PageSnapshot>(new PageSnapshot
            {
                PageId = page.Id,
                Version = page.Version,
                Width = _canvasSize.Width,
                Height = _canvasSize.Height,
                Objects = summaries,
            });
        }, ct);

    public Task<SessionOperationResult> AddShapeAsync(
        ShapeObject shape,
        string? operationId = null,
        string? groupId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return AddObjectAsync(shape, page => new AddShapeCommand(shape, page), operationId, groupId, ct);
    }

    public Task<SessionOperationResult> AddImageAsync(
        ImageObject image,
        string? operationId = null,
        string? groupId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        return AddObjectAsync(image, page => new AddImageCommand(image, page), operationId, groupId, ct);
    }

    public Task<SessionOperationResult> AddPathAsync(
        PathObject path,
        string? operationId = null,
        string? groupId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        return AddObjectAsync(path, page => new AddPathCommand(path, page), operationId, groupId, ct);
    }

    public Task<SessionOperationResult> AddTextObjectAsync(
        TextObject text,
        string? operationId = null,
        string? groupId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return AddObjectAsync(text, page => new AddTextCommand(text, page), operationId, groupId, ct);
    }

    private Task<SessionOperationResult> AddObjectAsync(
        WhiteboardObject obj,
        Func<PageModel, IWhiteboardCommand> createCommand,
        string? operationId,
        string? groupId,
        CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var page = _pages.Current;
            var evt = new ObjectAddedEvent(
                Guid.NewGuid().ToString("N"),
                page.Id,
                obj.Id.ToString(),
                "local",
                DateTimeOffset.UtcNow,
                PageSerializer.SerializeObject(obj),
                operationId ?? Guid.NewGuid().ToString("N"),
                groupId);

            _history.Execute(createCommand(page));
            PublishStateChanged();
            DispatchLocalEvent(evt);
            return CreateChangedResult(page, [obj.Id]);
        }, ct);

    public Task<SessionOperationResult> ClearCurrentPageAsync(
        string? operationId = null,
        CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            var page = _pages.Current;
            var affectedIds = page.GetAllObjects().Select(obj => obj.Id).ToArray();
            if (affectedIds.Length == 0)
                return CreateUnchangedResult(page);

            _history.Execute(new ClearPageCommand(page));
            RemoveSelection(affectedIds);
            PublishStateChanged();
            DispatchLocalEvent(new PageClearedEvent(
                Guid.NewGuid().ToString("N"),
                page.Id,
                "",
                "local",
                DateTimeOffset.UtcNow,
                operationId ?? Guid.NewGuid().ToString("N")));
            return CreateChangedResult(page, affectedIds);
        }, ct);

    public Task<SessionOperationResult> UndoAsync(CancellationToken ct = default) =>
        ExecuteAsync(() =>
        {
            var page = _pages.Current;
            if (!_history.CanUndo)
                return new ValueTask<SessionOperationResult>(CreateUnchangedResult(page));

            _history.Undo();
            NormalizeSelection(page);
            PublishStateChanged();
            return new ValueTask<SessionOperationResult>(CreateChangedResult(page, []));
        }, ct);

    public Task<SessionOperationResult> RedoAsync(CancellationToken ct = default) =>
        ExecuteAsync(() =>
        {
            var page = _pages.Current;
            if (!_history.CanRedo)
                return new ValueTask<SessionOperationResult>(CreateUnchangedResult(page));

            _history.Redo();
            NormalizeSelection(page);
            PublishStateChanged();
            return new ValueTask<SessionOperationResult>(CreateChangedResult(page, []));
        }, ct);

    public Task<SessionOperationResult> DeleteObjectsAsync(
        IReadOnlyList<Guid> objectIds,
        string? operationId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(objectIds);
        var ids = objectIds.Distinct().ToArray();
        return ExecuteAsync(async () =>
        {
            var page = _pages.Current;
            var objects = ids.Select(page.GetObject).OfType<WhiteboardObject>().ToArray();
            if (objects.Length == 0)
                return CreateUnchangedResult(page);

            foreach (var obj in objects)
                _history.Execute(new DeleteObjectCommand(obj, page));

            RemoveSelection(objects.Select(obj => obj.Id));
            PublishStateChanged();
            foreach (var obj in objects)
            {
                DispatchLocalEvent(new ObjectDeletedEvent(
                    Guid.NewGuid().ToString("N"),
                    page.Id,
                    obj.Id.ToString(),
                    "local",
                    DateTimeOffset.UtcNow));
            }

            return CreateChangedResult(page, objects.Select(obj => obj.Id).ToArray());
        }, ct);
    }

    public Task<SessionOperationResult> MoveObjectsAsync(
        IReadOnlyList<Guid> objectIds,
        float dx,
        float dy,
        string? operationId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(objectIds);
        var ids = objectIds.Distinct().ToArray();
        return ExecuteAsync(async () =>
        {
            var page = _pages.Current;
            var objects = ids.Select(page.GetObject).OfType<WhiteboardObject>().ToArray();
            if (objects.Length == 0)
                return CreateUnchangedResult(page);

            _history.Execute(new TransformCommand(objects, page, dx, dy));
            PublishStateChanged();
            foreach (var obj in objects)
            {
                DispatchLocalEvent(new ObjectTransformedEvent(
                    Guid.NewGuid().ToString("N"),
                    page.Id,
                    obj.Id.ToString(),
                    "local",
                    DateTimeOffset.UtcNow,
                    (float[])obj.Transform.Clone()));
            }
            return CreateChangedResult(page, objects.Select(obj => obj.Id).ToArray());
        }, ct);
    }

    public Task<SessionOperationResult> SelectObjectsInRegionAsync(
        float x,
        float y,
        float w,
        float h,
        CancellationToken ct = default) =>
        ExecuteAsync(() =>
        {
            var page = _pages.Current;
            var selected = page.LassoSelect(x, y, x + w, y + h)
                .Select(obj => obj.Id)
                .ToArray();
            _selection.SelectAll(selected);
            PublishStateChanged();
            return new ValueTask<SessionOperationResult>(CreateChangedResult(page, selected));
        }, ct);

    /// <summary>在执行器内读取当前选中对象 ID 的不可变副本。</summary>
    public Task<IReadOnlyList<Guid>> GetSelectedObjectIdsAsync(CancellationToken ct = default) =>
        ExecuteAsync(() => new ValueTask<IReadOnlyList<Guid>>(_selection.ToList()), ct);

    /// <summary>在执行器内替换当前选中对象集合。</summary>
    public Task SetSelectedObjectIdsAsync(IEnumerable<Guid> objectIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(objectIds);
        var ids = objectIds.Distinct().ToArray();
        return ExecuteAsync(() =>
        {
            _selection.SelectAll(ids.Where(id => _pages.Current.GetObject(id) is not null));
            PublishStateChanged();
            return ValueTask.CompletedTask;
        }, ct);
    }

    public Task ClearSelectionAsync(CancellationToken ct = default) =>
        ExecuteAsync(() =>
        {
            if (_selection.IsEmpty)
                return ValueTask.CompletedTask;

            _selection.Clear();
            PublishStateChanged();
            return ValueTask.CompletedTask;
        }, ct);

    public Task<SessionOperationResult> BringToFrontAsync(
        IReadOnlyList<Guid> objectIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(objectIds);
        var ids = objectIds.Distinct().ToArray();
        return ExecuteAsync(() =>
        {
            var page = _pages.Current;
            var objects = ids.Select(page.GetObject).OfType<WhiteboardObject>().ToArray();
            if (objects.Length == 0)
                return new ValueTask<SessionOperationResult>(CreateUnchangedResult(page));

            var zIndex = page.GetAllObjects().Select(obj => obj.ZIndex).DefaultIfEmpty(0).Max() + 1;
            foreach (var obj in objects)
                obj.ZIndex = zIndex++;
            page.MarkContentChanged();

            PublishStateChanged();
            return new ValueTask<SessionOperationResult>(CreateChangedResult(page, objects.Select(obj => obj.Id).ToArray()));
        }, ct);
    }

    public Task<SessionOperationResult> CopyObjectsAsync(
        IReadOnlyList<Guid> objectIds,
        float dx = 20f,
        float dy = 20f,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(objectIds);
        var ids = objectIds.Distinct().ToArray();
        return ExecuteAsync(() =>
        {
            var page = _pages.Current;
            var newIds = new List<Guid>();
            foreach (var id in ids)
            {
                var obj = page.GetObject(id);
                if (obj is null)
                    continue;

                var copy = PageSerializer.DeserializeObject(
                    PageSerializer.CloneObjectWithOffset(PageSerializer.SerializeObject(obj), dx, dy));
                if (copy is null)
                    continue;

                _history.Execute(new AddObjectCommand(copy, page));
                newIds.Add(copy.Id);
            }

            if (newIds.Count == 0)
                return new ValueTask<SessionOperationResult>(CreateUnchangedResult(page));

            PublishStateChanged();
            return new ValueTask<SessionOperationResult>(CreateChangedResult(page, newIds));
        }, ct);
    }

    public Task<SessionOperationResult> ResizeBoundsAsync(
        IReadOnlyList<(Guid Id, float X, float Y, float W, float H)> updates,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(updates);
        var requestedUpdates = updates.ToArray();
        return ExecuteAsync(() =>
        {
            var page = _pages.Current;
            var pairs = requestedUpdates
                .Select(update => (Object: page.GetObject(update.Id), update.X, update.Y, update.W, update.H))
                .Where(pair => pair.Object is not null)
                .ToArray();
            if (pairs.Length == 0)
                return new ValueTask<SessionOperationResult>(CreateUnchangedResult(page));

            var objects = pairs.Select(pair => pair.Object!).ToArray();
            var bounds = pairs.Select(pair => (pair.X, pair.Y, pair.W, pair.H)).ToArray();
            _history.Execute(new ResizeBoundsCommand(objects, bounds, page));
            PublishStateChanged();
            return new ValueTask<SessionOperationResult>(CreateChangedResult(page, objects.Select(obj => obj.Id).ToArray()));
        }, ct);
    }

    /// <summary>将对象绕文档坐标系内的轴心点顺时针旋转指定角度。</summary>
    public Task<SessionOperationResult> RotateObjectsAsync(
        IReadOnlyList<Guid> objectIds,
        float angleDeg,
        float pivotDocX,
        float pivotDocY,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(objectIds);
        var ids = objectIds.Distinct().ToArray();
        return ExecuteAsync(() => RotateObjectsCore(ids, angleDeg, pivotDocX, pivotDocY), ct);
    }

    private ValueTask<SessionOperationResult> RotateObjectsCore(
        IReadOnlyList<Guid> objectIds,
        float angleDeg,
        float pivotDocX,
        float pivotDocY)
    {
        var page = _pages.Current;
        var objects = objectIds.Select(page.GetObject).OfType<WhiteboardObject>().ToArray();
        if (objects.Length == 0)
            return new ValueTask<SessionOperationResult>(CreateUnchangedResult(page));

        var radians = angleDeg * MathF.PI / 180f;
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        var newStates = objects.Select(obj => CreateRotationState(obj, cos, sin, pivotDocX, pivotDocY)).ToArray();
        _history.Execute(new RotateBoundsCommand(objects, newStates, page));
        PublishStateChanged();
        return new ValueTask<SessionOperationResult>(CreateChangedResult(page, objects.Select(obj => obj.Id).ToArray()));
    }

    private static (float X, float Y, float W, float H, float[] Transform) CreateRotationState(
        WhiteboardObject obj,
        float cos,
        float sin,
        float pivotDocX,
        float pivotDocY)
    {
        var transform = obj.Transform;
        var rotated = new float[]
        {
            transform[0] * cos - transform[1] * sin,
            transform[0] * sin + transform[1] * cos,
            transform[2] * cos - transform[3] * sin,
            transform[2] * sin + transform[3] * cos,
            transform[4] * cos - transform[5] * sin + pivotDocX * (1 - cos) + pivotDocY * sin,
            transform[4] * sin + transform[5] * cos + pivotDocY * (1 - cos) - pivotDocX * sin,
        };
        var (x, y, w, h) = ComputeWorldAABB(
            obj.LocalBoundsX,
            obj.LocalBoundsY,
            obj.LocalBoundsW,
            obj.LocalBoundsH,
            rotated);
        return (x, y, w, h, rotated);
    }

    private SessionOperationResult CreateChangedResult(PageModel page, IReadOnlyList<Guid> affectedObjectIds) =>
        new(page.Id, page.Version, affectedObjectIds, true);

    private SessionOperationResult CreateUnchangedResult(PageModel page) =>
        new(page.Id, page.Version, [], false);

    private void RemoveSelection(IEnumerable<Guid> objectIds)
    {
        var removed = objectIds.ToHashSet();
        if (removed.Count == 0)
            return;

        _selection.SelectAll(_selection.ToList().Where(id => !removed.Contains(id)));
    }

    private void NormalizeSelection(PageModel page) =>
        _selection.SelectAll(_selection.ToList().Where(id => page.GetObject(id) is not null));

    private static (float x, float y, float w, float h) ComputeWorldAABB(
        float lx,
        float ly,
        float lw,
        float lh,
        float[] t
    )
    {
        // WPF 行向量约定：x' = x*t[0] + y*t[2] + t[4]，y' = x*t[1] + y*t[3] + t[5]
        Span<(float, float)> corners = stackalloc (float, float)[4]
        {
            (lx * t[0] + ly * t[2] + t[4], lx * t[1] + ly * t[3] + t[5]),
            ((lx + lw) * t[0] + ly * t[2] + t[4], (lx + lw) * t[1] + ly * t[3] + t[5]),
            (lx * t[0] + (ly + lh) * t[2] + t[4], lx * t[1] + (ly + lh) * t[3] + t[5]),
            (
                (lx + lw) * t[0] + (ly + lh) * t[2] + t[4],
                (lx + lw) * t[1] + (ly + lh) * t[3] + t[5]
            ),
        };
        float minX = corners[0].Item1,
            minY = corners[0].Item2;
        float maxX = minX,
            maxY = minY;
        for (int i = 1; i < 4; i++)
        {
            minX = MathF.Min(minX, corners[i].Item1);
            minY = MathF.Min(minY, corners[i].Item2);
            maxX = MathF.Max(maxX, corners[i].Item1);
            maxY = MathF.Max(maxY, corners[i].Item2);
        }
        return (minX, minY, maxX - minX, maxY - minY);
    }

    private static (float x, float y, float w, float h) RotatedAABB(
        float bx,
        float by,
        float bw,
        float bh,
        float cos,
        float sin,
        float px,
        float py
    )
    {
        Span<(float, float)> corners = stackalloc (float, float)[4]
        {
            RotPt(bx, by, cos, sin, px, py),
            RotPt(bx + bw, by, cos, sin, px, py),
            RotPt(bx, by + bh, cos, sin, px, py),
            RotPt(bx + bw, by + bh, cos, sin, px, py),
        };
        float minX = corners[0].Item1,
            minY = corners[0].Item2;
        float maxX = minX,
            maxY = minY;
        for (int i = 1; i < 4; i++)
        {
            minX = MathF.Min(minX, corners[i].Item1);
            minY = MathF.Min(minY, corners[i].Item2);
            maxX = MathF.Max(maxX, corners[i].Item1);
            maxY = MathF.Max(maxY, corners[i].Item2);
        }
        return (minX, minY, maxX - minX, maxY - minY);
    }

    private static (float x, float y) RotPt(
        float x,
        float y,
        float cos,
        float sin,
        float px,
        float py
    )
    {
        float dx = x - px,
            dy = y - py;
        return (px + dx * cos - dy * sin, py + dx * sin + dy * cos);
    }

    private async Task RunExecutorAsync(CancellationToken ct)
    {
        await foreach (var op in _ops.Reader.ReadAllAsync(ct))
        {
            try
            {
                await op();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 单次操作失败不中断执行器；实际项目中接入 IHostLogger
                System.Diagnostics.Debug.WriteLine($"[Session] Op failed: {ex.Message}");
            }
        }
    }

    private async Task StartRemoteListenAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _collab.RemoteEventsAsync(ct))
            {
                if (ct.IsCancellationRequested)
                    break;
                await EnqueueAsync(() =>
                {
                    if (ApplyRemoteEvent(evt))
                        PublishStateChanged();
                    return ValueTask.CompletedTask;
                }, ct);
            }
        }
        catch (ChannelClosedException)
        {
            // StopAsync 关闭写端后，适配器仍可能并发 yield 一个远端事件。
        }
    }

    private bool ApplyRemoteEvent(WhiteboardEvent evt)
    {
        // 远端事件不进入本地历史，避免远端操作被本地 Undo/Redo 回滚而导致状态分叉。
        // 所有变更均由执行器串行完成；若作用于当前页，则同步清理失效选择。
        var changed = evt switch
        {
            StrokeAddedEvent added => TryAddRemoteObject(added.PageId, added.StrokeData),
            ObjectAddedEvent added => TryAddRemoteObject(added.PageId, added.ObjectData),
            ObjectDeletedEvent deleted => TryDeleteRemoteObject(deleted.PageId, deleted.ObjectId),
            ObjectTransformedEvent transformed => TryTransformRemoteObject(
                transformed.PageId, transformed.ObjectId, transformed.Matrix6),
            ObjectsTransformedEvent transformed => TryTransformRemoteObjects(
                transformed.PageId, transformed.ObjectIds, transformed.Matrix6),
            PageClearedEvent cleared => TryClearRemotePage(cleared.PageId),
            PageAddedEvent => false,
            _ => false,
        };

        if (changed)
            NormalizeSelection(_pages.Current);
        return changed;
    }

    private bool TryAddRemoteObject(string pageId, byte[] data)
    {
        if (data.Length == 0 || _pages.GetPage(pageId) is not { } page)
            return false;

        var obj = PageSerializer.DeserializeObject(data);
        if (obj is null || page.GetObject(obj.Id) is not null)
            return false;

        page.AddObject(obj);
        return true;
    }

    private bool TryDeleteRemoteObject(string pageId, string objectId)
    {
        if (!Guid.TryParse(objectId, out var id) || _pages.GetPage(pageId) is not { } page)
            return false;

        if (page.GetObject(id) is not { } obj)
            return false;

        page.RemoveObject(obj);
        return true;
    }

    private static bool HasValidMatrix(float[] matrix) =>
        matrix is { Length: 6 } && matrix.All(float.IsFinite);

    private bool TryTransformRemoteObject(string pageId, string objectId, float[] matrix)
    {
        if (!Guid.TryParse(objectId, out var id) || !HasValidMatrix(matrix) ||
            _pages.GetPage(pageId) is not { } page || page.GetObject(id) is not { } obj ||
            !TryComputeRemoteTransformBounds(obj, matrix, out var bounds))
            return false;

        page.SpatialIndex.Update(obj, () =>
            obj.SetWorldBoundsAndTransform(bounds.X, bounds.Y, bounds.Width, bounds.Height, matrix));
        page.MarkContentChanged();
        return true;
    }

    private bool TryTransformRemoteObjects(string pageId, IReadOnlyList<string> objectIds, float[] matrix)
    {
        if (!HasValidMatrix(matrix) || _pages.GetPage(pageId) is not { } page)
            return false;

        var objects = objectIds
            .Select(id => Guid.TryParse(id, out var guid) ? page.GetObject(guid) : null)
            .OfType<WhiteboardObject>()
            .ToArray();
        if (objects.Length == 0)
            return false;

        var transforms = new List<(WhiteboardObject Object, (float X, float Y, float Width, float Height) Bounds)>();
        foreach (var obj in objects)
        {
            if (!TryComputeRemoteTransformBounds(obj, matrix, out var bounds))
                return false;
            transforms.Add((obj, bounds));
        }

        foreach (var transform in transforms)
        {
            page.SpatialIndex.Update(transform.Object, () =>
                transform.Object.SetWorldBoundsAndTransform(
                    transform.Bounds.X,
                    transform.Bounds.Y,
                    transform.Bounds.Width,
                    transform.Bounds.Height,
                    matrix));
        }
        page.MarkContentChanged();
        return true;
    }

    private static bool TryComputeRemoteTransformBounds(
        WhiteboardObject obj,
        float[] matrix,
        out (float X, float Y, float Width, float Height) bounds)
    {
        bounds = ComputeWorldAABB(
            obj.LocalBoundsX,
            obj.LocalBoundsY,
            obj.LocalBoundsW,
            obj.LocalBoundsH,
            matrix);
        return WhiteboardGeometry.HasValidBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private bool TryClearRemotePage(string pageId)
    {
        if (_pages.GetPage(pageId) is not { } page || page.GetAllObjects().Count == 0)
            return false;

        page.Clear();
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }
}

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using WhiteBoard.Contracts.Models;
using WhiteBoard.Contracts.Services;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;
using WhiteBoard.Core.Services;

namespace WhiteBoard.Core.Tests;

public sealed class WhiteboardSessionConcurrencyTests
{
    [Fact]
    public async Task QueuedNavigationThenAddShape_UsesThePageCurrentWhenTheEditExecutes()
    {
        var collaboration = new BlockingCollaborationAdapter();
        await using var session = new WhiteboardSession(collaboration);
        await session.StartAsync();

        var firstAdd = session.AddShapeAsync(CreateShape());
        await collaboration.FirstLocalEventStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondPageTask = session.AddPageAsync("第二页");
        var navigate = session.GoToPageAsync(1);
        var addedShape = CreateShape();
        var add = session.AddShapeAsync(addedShape);

        collaboration.ReleaseFirstLocalEvent();
        await firstAdd;
        var secondPage = await secondPageTask;
        Assert.True(await navigate);
        var addedResult = await add;

        Assert.True(addedResult.Changed);
        Assert.Equal(secondPage.Id, addedResult.PageId);

        var document = await session.CaptureDocumentAsync();
        var activePage = PageSerializer.Deserialize(document.Pages[1].Data, document.Pages[1].Id);
        Assert.Contains(activePage.GetAllObjects(), obj => obj.Id == addedShape.Id);

        var addedEvent = Assert.Single(
            collaboration.LocalEvents.OfType<ObjectAddedEvent>(),
            evt => evt.ObjectId == addedShape.Id.ToString());
        Assert.Equal(secondPage.Id, addedEvent.PageId);
    }

    [Fact]
    public async Task AcceptedOperation_CompletesAfterCancellationAndReturnsItsActualResult()
    {
        var collaboration = new BlockingCollaborationAdapter();
        await using var session = new WhiteboardSession(collaboration);
        await session.StartAsync();

        var firstAdd = session.AddShapeAsync(CreateShape());
        await collaboration.FirstLocalEventStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var cancellation = new CancellationTokenSource();
        var addedShape = CreateShape();
        var add = session.AddShapeAsync(addedShape, ct: cancellation.Token);
        cancellation.Cancel();

        collaboration.ReleaseFirstLocalEvent();
        await firstAdd;
        var result = await add;

        Assert.True(result.Changed);
        Assert.Contains(addedShape.Id, result.AffectedObjectIds);

        var document = await session.CaptureDocumentAsync();
        var currentPage = PageSerializer.Deserialize(document.Pages[0].Data, document.Pages[0].Id);
        Assert.Contains(currentPage.GetAllObjects(), obj => obj.Id == addedShape.Id);
    }

    [Fact]
    public async Task PreCancelledOperation_IsNotEnqueuedOrApplied()
    {
        await using var session = new WhiteboardSession();
        await session.StartAsync();
        var before = await session.CaptureUiSnapshotAsync();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.AddShapeAsync(CreateShape(), ct: cancellation.Token));

        var after = await session.CaptureUiSnapshotAsync();
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.CurrentPage.Data, after.CurrentPage.Data);
    }

    [Fact]
    public async Task UiSnapshot_IsDetachedAndCapturesAConsistentHistoryAndSelection()
    {
        await using var session = new WhiteboardSession();
        await session.StartAsync();

        var shape = CreateShape();
        await session.AddShapeAsync(shape);
        await session.SetSelectedObjectIdsAsync([shape.Id]);
        var snapshot = await session.CaptureUiSnapshotAsync();

        await session.DeleteObjectsAsync([shape.Id]);
        var detachedPage = PageSerializer.Deserialize(
            snapshot.CurrentPage.Data,
            snapshot.CurrentPage.PageId,
            snapshot.CurrentPage.Title);

        Assert.True(snapshot.History.CanUndo);
        Assert.Contains(shape.Id, snapshot.SelectedObjectIds);
        Assert.Contains(detachedPage.GetAllObjects(), obj => obj.Id == shape.Id);
        Assert.True((await session.CaptureUiSnapshotAsync()).Revision > snapshot.Revision);
    }

    [Fact]
    public async Task AwaitedUndoAndRedo_HaveUpdatedPageAndHistoryState()
    {
        await using var session = new WhiteboardSession();
        await session.StartAsync();

        var shape = CreateShape();
        await session.AddShapeAsync(shape);
        await session.UndoAsync();
        var afterUndo = await session.CaptureSnapshotAsync();
        await session.RedoAsync();
        var afterRedo = await session.CaptureSnapshotAsync();

        Assert.DoesNotContain(afterUndo.Objects, obj => obj.Id == shape.Id);
        Assert.Contains(afterRedo.Objects, obj => obj.Id == shape.Id);
    }

    [Fact]
    public async Task StartAsync_CannotBeCalledMoreThanOnce()
    {
        await using var session = new WhiteboardSession();
        await session.StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync());
    }

    [Fact]
    public async Task StopAsync_CompletesAcceptedOperationsWhenCollaborationSendNeverCompletes()
    {
        var collaboration = new BlockingCollaborationAdapter();
        await using var session = new WhiteboardSession(collaboration);
        await session.StartAsync();

        var first = session.AddShapeAsync(CreateShape());
        await collaboration.FirstLocalEventStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = session.AddShapeAsync(CreateShape());

        await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await first.WaitAsync(TimeSpan.FromSeconds(5))).Changed);
        Assert.True((await second.WaitAsync(TimeSpan.FromSeconds(5))).Changed);
    }

    [Fact]
    public async Task LocalCollaborationFailure_DoesNotHideCommittedStateChange()
    {
        await using var session = new WhiteboardSession(new ThrowingCollaborationAdapter());
        await session.StartAsync();
        var before = await session.CaptureUiSnapshotAsync();
        var shape = CreateShape();

        var result = await session.AddShapeAsync(shape);
        var after = await session.CaptureUiSnapshotAsync();
        var snapshot = await session.CaptureSnapshotAsync();

        Assert.True(result.Changed);
        Assert.True(after.Revision > before.Revision);
        Assert.Contains(snapshot.Objects, obj => obj.Id == shape.Id);
    }

    [Fact]
    public async Task PageVersion_IncrementsForContentEditsAndUndoRedo()
    {
        await using var session = new WhiteboardSession();
        await session.StartAsync();
        var shape = CreateShape();

        var added = await session.AddShapeAsync(shape);
        var moved = await session.MoveObjectsAsync([shape.Id], 10f, 0f);
        var resized = await session.ResizeBoundsAsync([(shape.Id, 20f, 20f, 120f, 90f)]);
        var rotated = await session.RotateObjectsAsync([shape.Id], 15f, 50f, 50f);
        var broughtToFront = await session.BringToFrontAsync([shape.Id]);
        var undone = await session.UndoAsync();
        var redone = await session.RedoAsync();

        Assert.True(moved.PageVersion > added.PageVersion);
        Assert.True(resized.PageVersion > moved.PageVersion);
        Assert.True(rotated.PageVersion > resized.PageVersion);
        Assert.True(broughtToFront.PageVersion > rotated.PageVersion);
        Assert.True(undone.PageVersion > broughtToFront.PageVersion);
        Assert.True(redone.PageVersion > undone.PageVersion);
    }

    [Fact]
    public async Task MoveObjects_DispatchesSupportedAbsoluteTransformEvents()
    {
        var collaboration = new RemoteEventCollaborationAdapter();
        await using var session = new WhiteboardSession(collaboration);
        await session.StartAsync();

        var first = CreateShape();
        var second = CreateShape();
        await session.AddShapeAsync(first);
        await session.AddShapeAsync(second);
        await session.MoveObjectsAsync([first.Id, second.Id], 75f, 25f);

        var events = collaboration.LocalEvents.OfType<ObjectTransformedEvent>().ToArray();
        Assert.Equal(2, events.Length);
        Assert.All(events, evt =>
        {
            Assert.Contains(evt.ObjectId, new[] { first.Id.ToString(), second.Id.ToString() });
            Assert.Equal([1f, 0f, 0f, 1f, 75f, 25f], evt.Matrix6);
        });
    }

    [Fact]
    public async Task RemoteTransform_UpdatesBoundsSpatialIndexAndPageVersion()
    {
        var collaboration = new RemoteEventCollaborationAdapter();
        await using var session = new WhiteboardSession(collaboration);
        await session.StartAsync();

        var initial = await session.CaptureUiSnapshotAsync();
        var shape = CreateShape();
        await PublishAndWaitAsync(session, collaboration, initial.Revision + 1,
            new ObjectAddedEvent(
                "remote-add",
                initial.CurrentPage.PageId,
                shape.Id.ToString(),
                "remote",
                DateTimeOffset.UtcNow,
                PageSerializer.SerializeObject(shape),
                "operation-add",
                null));

        var added = await session.CaptureSnapshotAsync();
        await PublishAndWaitAsync(session, collaboration, (await session.CaptureUiSnapshotAsync()).Revision + 1,
            new ObjectTransformedEvent(
                "remote-transform",
                added.PageId,
                shape.Id.ToString(),
                "remote",
                DateTimeOffset.UtcNow,
                [1f, 0f, 0f, 1f, 200f, 150f]));

        var transformed = await session.CaptureSnapshotAsync();
        var summary = Assert.Single(transformed.Objects);
        Assert.Equal(210f, summary.X);
        Assert.Equal(170f, summary.Y);
        Assert.True(transformed.Version > added.Version);

        var selected = await session.SelectObjectsInRegionAsync(205f, 165f, 120f, 100f);
        Assert.Equal([shape.Id], selected.AffectedObjectIds);
    }

    [Fact]
    public async Task RemoteBatchTransform_UpdatesEveryObjectAndSpatialIndex()
    {
        var collaboration = new RemoteEventCollaborationAdapter();
        await using var session = new WhiteboardSession(collaboration);
        await session.StartAsync();

        var page = await session.CaptureSnapshotAsync();
        var first = CreateShape();
        var second = CreateShape();
        await PublishAndWaitAsync(session, collaboration, (await session.CaptureUiSnapshotAsync()).Revision + 1,
            CreateRemoteAddEvent(page.PageId, first, "remote-add-first"));
        await PublishAndWaitAsync(session, collaboration, (await session.CaptureUiSnapshotAsync()).Revision + 1,
            CreateRemoteAddEvent(page.PageId, second, "remote-add-second"));

        var beforeTransform = await session.CaptureSnapshotAsync();
        await PublishAndWaitAsync(session, collaboration, (await session.CaptureUiSnapshotAsync()).Revision + 1,
            new ObjectsTransformedEvent(
                "remote-batch-transform",
                page.PageId,
                "",
                "remote",
                DateTimeOffset.UtcNow,
                [first.Id.ToString(), second.Id.ToString()],
                [1f, 0f, 0f, 1f, 300f, 250f],
                "operation-transform"));

        var afterTransform = await session.CaptureSnapshotAsync();
        Assert.True(afterTransform.Version > beforeTransform.Version);
        Assert.All(afterTransform.Objects, obj =>
        {
            Assert.Equal(310f, obj.X);
            Assert.Equal(270f, obj.Y);
        });

        var selected = await session.SelectObjectsInRegionAsync(305f, 265f, 120f, 100f);
        Assert.Equal(2, selected.AffectedObjectIds.Count);
        Assert.Contains(first.Id, selected.AffectedObjectIds);
        Assert.Contains(second.Id, selected.AffectedObjectIds);
    }

    [Fact]
    public async Task RemoteDeleteAndClear_NormalizeCurrentPageSelection()
    {
        var collaboration = new RemoteEventCollaborationAdapter();
        await using var session = new WhiteboardSession(collaboration);
        await session.StartAsync();

        var page = await session.CaptureSnapshotAsync();
        var first = CreateShape();
        var second = CreateShape();
        await PublishAndWaitAsync(session, collaboration, (await session.CaptureUiSnapshotAsync()).Revision + 1,
            CreateRemoteAddEvent(page.PageId, first, "remote-add-first"));
        await PublishAndWaitAsync(session, collaboration, (await session.CaptureUiSnapshotAsync()).Revision + 1,
            CreateRemoteAddEvent(page.PageId, second, "remote-add-second"));
        await session.SetSelectedObjectIdsAsync([first.Id, second.Id]);

        await PublishAndWaitAsync(session, collaboration, (await session.CaptureUiSnapshotAsync()).Revision + 1,
            new ObjectDeletedEvent(
                "remote-delete",
                page.PageId,
                first.Id.ToString(),
                "remote",
                DateTimeOffset.UtcNow));
        var afterDelete = await session.CaptureUiSnapshotAsync();
        Assert.Equal([second.Id], afterDelete.SelectedObjectIds);

        await PublishAndWaitAsync(session, collaboration, afterDelete.Revision + 1,
            new PageClearedEvent(
                "remote-clear",
                page.PageId,
                "",
                "remote",
                DateTimeOffset.UtcNow,
                "operation-clear"));
        var afterClear = await session.CaptureUiSnapshotAsync();
        Assert.Empty(afterClear.SelectedObjectIds);
        Assert.Empty((await session.CaptureSnapshotAsync()).Objects);
    }

    private static ObjectAddedEvent CreateRemoteAddEvent(string pageId, ShapeObject shape, string eventId) => new(
        eventId,
        pageId,
        shape.Id.ToString(),
        "remote",
        DateTimeOffset.UtcNow,
        PageSerializer.SerializeObject(shape),
        eventId,
        null);

    private static async Task PublishAndWaitAsync(
        WhiteboardSession session,
        RemoteEventCollaborationAdapter collaboration,
        long expectedRevision,
        WhiteboardEvent evt)
    {
        var applied = WaitForRevisionAsync(session, expectedRevision);
        collaboration.Publish(evt);
        await applied;
    }

    private static async Task WaitForRevisionAsync(WhiteboardSession session, long expectedRevision)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStateChanged(long revision)
        {
            if (revision >= expectedRevision)
                completed.TrySetResult();
        }

        session.StateChanged += OnStateChanged;
        try
        {
            if ((await session.CaptureUiSnapshotAsync()).Revision >= expectedRevision)
                return;

            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            session.StateChanged -= OnStateChanged;
        }
    }

    private static ShapeObject CreateShape() => new(
        ShapeType.Rectangle,
        0xFFFFFFFF,
        0xFF000000,
        1f,
        true,
        10f,
        20f,
        100f,
        80f);

    private sealed class RemoteEventCollaborationAdapter : ICollaborationAdapter
    {
        private readonly Channel<WhiteboardEvent> _remoteEvents = Channel.CreateUnbounded<WhiteboardEvent>();

        public bool IsActive => true;
        public List<WhiteboardEvent> LocalEvents { get; } = [];

        public ValueTask OnLocalEventAsync(WhiteboardEvent evt)
        {
            LocalEvents.Add(evt);
            return default;
        }

        public void Publish(WhiteboardEvent evt) =>
            _remoteEvents.Writer.TryWrite(evt);

        public async IAsyncEnumerable<WhiteboardEvent> RemoteEventsAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var evt in _remoteEvents.Reader.ReadAllAsync(ct))
                yield return evt;
        }
    }

    private sealed class ThrowingCollaborationAdapter : ICollaborationAdapter
    {
        public bool IsActive => true;

        public ValueTask OnLocalEventAsync(WhiteboardEvent evt) =>
            ValueTask.FromException(new InvalidOperationException("模拟协作发送失败。"));

        public async IAsyncEnumerable<WhiteboardEvent> RemoteEventsAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            yield break;
        }
    }

    private sealed class BlockingCollaborationAdapter : ICollaborationAdapter
    {
        private readonly TaskCompletionSource _firstLocalEventStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstLocalEvent = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _localEventCount;

        public bool IsActive => true;
        public TaskCompletionSource FirstLocalEventStarted => _firstLocalEventStarted;
        public List<WhiteboardEvent> LocalEvents { get; } = [];

        public async ValueTask OnLocalEventAsync(WhiteboardEvent evt)
        {
            LocalEvents.Add(evt);
            if (Interlocked.Increment(ref _localEventCount) != 1)
                return;

            _firstLocalEventStarted.TrySetResult();
            await _releaseFirstLocalEvent.Task;
        }

        public void ReleaseFirstLocalEvent() => _releaseFirstLocalEvent.TrySetResult();

        public async IAsyncEnumerable<WhiteboardEvent> RemoteEventsAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            yield break;
        }
    }
}

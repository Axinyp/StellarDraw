using WhiteBoard.AI.Services;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.AI.Tests;

public sealed class WhiteboardAiActionsTests
{
    [Fact]
    public async Task InsertShape_ReturnsMetadataFromTheCompletedSessionOperation()
    {
        await using var session = new WhiteboardSession();
        await session.StartAsync();
        var actions = new WhiteboardAiActionsImpl(session);

        var result = await actions.InsertShapeAsync(new ShapeSpec(
            "rectangle",
            20f,
            30f,
            120f,
            80f),
            alias: "card");
        var snapshot = await session.CaptureSnapshotAsync();

        Assert.Equal("ok", result.Status);
        Assert.Equal(snapshot.PageId, result.PageId);
        Assert.Equal(snapshot.Version, result.PageVersion);
        var created = Assert.Single(result.CreatedObjectIds);
        Assert.Equal(created, result.Aliases["card"]);
    }

    [Fact]
    public async Task NavigateToPage_ReturnsThePageBoundToTheNavigationOperation()
    {
        await using var session = new WhiteboardSession();
        await session.StartAsync();
        var actions = new WhiteboardAiActionsImpl(session);

        var added = await actions.AddPageAsync("第二页");
        var navigated = await actions.NavigateToPageAsync(0);
        var snapshot = await session.CaptureSnapshotAsync();

        Assert.Equal("ok", added.Status);
        Assert.Equal("ok", navigated.Status);
        Assert.NotEqual(added.PageId, navigated.PageId);
        Assert.Equal(snapshot.PageId, navigated.PageId);
        Assert.Equal(snapshot.Version, navigated.PageVersion);
    }

    [Fact]
    public async Task Snapshot_ContainsAliasesRegisteredForLiveObjects()
    {
        await using var session = new WhiteboardSession();
        await session.StartAsync();
        var actions = new WhiteboardAiActionsImpl(session);

        var inserted = await actions.InsertShapeAsync(new ShapeSpec(
            "rectangle",
            20f,
            30f,
            120f,
            80f),
            alias: "card");
        var snapshot = await actions.GetSnapshotAsync();

        var createdId = Guid.Parse(Assert.Single(inserted.CreatedObjectIds));
        var obj = Assert.Single(snapshot.Objects);
        Assert.Equal(createdId, obj.Id);
        Assert.Equal("card", obj.Alias);
    }

    [Fact]
    public async Task DeleteMissingAlias_DoesNotReportTheRequestedIdAsDeleted()
    {
        await using var session = new WhiteboardSession();
        await session.StartAsync();
        var actions = new WhiteboardAiActionsImpl(session);
        var id = Guid.NewGuid().ToString();

        var result = await actions.DeleteObjectsAsync([id]);

        Assert.Equal("ok", result.Status);
        Assert.Empty(result.DeletedObjectIds);
    }
}

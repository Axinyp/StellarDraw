using WhiteBoard.Contracts.Hub;
using WhiteBoard.Contracts.Models;

namespace WhiteBoard.Protocol.Tests;

public class HubEnvelopeSerializerTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2025, 6, 9, 12, 0, 0, TimeSpan.Zero);

    // ── Wrap / discriminator ──────────────────────────────────────────────────

    [Fact]
    public void Wrap_StrokeAddedEvent_SetsCorrectDiscriminator()
    {
        var evt = new StrokeAddedEvent("e1", "p1", "o1", "c1", FixedTime, [1, 2, 3]);
        var env = HubEnvelopeSerializer.Wrap(evt);
        Assert.NotNull(env);
        Assert.Equal("StrokeAdded", env!.T);
    }

    [Fact]
    public void Wrap_ObjectDeletedEvent_SetsCorrectDiscriminator()
    {
        var evt = new ObjectDeletedEvent("e2", "p1", "o2", "c1", FixedTime);
        var env = HubEnvelopeSerializer.Wrap(evt);
        Assert.NotNull(env);
        Assert.Equal("ObjectDeleted", env!.T);
    }

    [Fact]
    public void Wrap_ObjectTransformedEvent_SetsCorrectDiscriminator()
    {
        var evt = new ObjectTransformedEvent("e3", "p1", "o3", "c1", FixedTime, [1, 0, 0, 1, 0, 0]);
        var env = HubEnvelopeSerializer.Wrap(evt);
        Assert.NotNull(env);
        Assert.Equal("ObjectTransformed", env!.T);
    }

    [Fact]
    public void Wrap_ObjectsTransformedEvent_SetsCorrectDiscriminator()
    {
        var evt = new ObjectsTransformedEvent(
            "e4", "p1", "", "c1", FixedTime, ["o1", "o2"], [1, 0, 0, 1, 5, 10], "op1");
        var env = HubEnvelopeSerializer.Wrap(evt);
        Assert.NotNull(env);
        Assert.Equal("ObjectsTransformed", env!.T);
    }

    [Fact]
    public void Wrap_ObjectAddedEvent_SetsCorrectDiscriminator()
    {
        var evt = new ObjectAddedEvent("e4", "p1", "o4", "c1", FixedTime, [1, 2], "op1", null);
        var env = HubEnvelopeSerializer.Wrap(evt);
        Assert.NotNull(env);
        Assert.Equal("ObjectAdded", env!.T);
    }

    [Fact]
    public void Wrap_PageClearedEvent_SetsCorrectDiscriminator()
    {
        var evt = new PageClearedEvent("e5", "p1", "", "c1", FixedTime, "op2");
        var env = HubEnvelopeSerializer.Wrap(evt);
        Assert.NotNull(env);
        Assert.Equal("PageCleared", env!.T);
    }

    [Fact]
    public void Wrap_PageAddedEvent_SetsCorrectDiscriminator()
    {
        var evt = new PageAddedEvent("e4", "p2", "", "c1", FixedTime, 1);
        var env = HubEnvelopeSerializer.Wrap(evt);
        Assert.NotNull(env);
        Assert.Equal("PageAdded", env!.T);
    }

    // ── Full round-trip ───────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_StrokeAddedEvent_PreservesAllFields()
    {
        var original = new StrokeAddedEvent("evt-1", "page-A", "obj-X", "client-1",
            FixedTime, [0xDE, 0xAD, 0xBE, 0xEF]);

        var json = HubEnvelopeSerializer.SerializeEnvelope(HubEnvelopeSerializer.Wrap(original)!);
        var env2 = HubEnvelopeSerializer.DeserializeEnvelope(json);
        var restored = HubEnvelopeSerializer.Unwrap(env2!) as StrokeAddedEvent;

        Assert.NotNull(restored);
        Assert.Equal(original.EventId, restored!.EventId);
        Assert.Equal(original.PageId, restored.PageId);
        Assert.Equal(original.ObjectId, restored.ObjectId);
        Assert.Equal(original.ClientId, restored.ClientId);
        Assert.Equal(original.OccurredAtUtc, restored.OccurredAtUtc);
        Assert.Equal(original.StrokeData, restored.StrokeData);
    }

    [Fact]
    public void RoundTrip_ObjectDeletedEvent_PreservesAllFields()
    {
        var original = new ObjectDeletedEvent("evt-2", "page-B", "obj-Y", "client-2", FixedTime);

        var json = HubEnvelopeSerializer.SerializeEnvelope(HubEnvelopeSerializer.Wrap(original)!);
        var restored = HubEnvelopeSerializer.Unwrap(HubEnvelopeSerializer.DeserializeEnvelope(json)!)
            as ObjectDeletedEvent;

        Assert.NotNull(restored);
        Assert.Equal(original.EventId, restored!.EventId);
        Assert.Equal(original.PageId, restored.PageId);
        Assert.Equal(original.ObjectId, restored.ObjectId);
    }

    [Fact]
    public void RoundTrip_ObjectTransformedEvent_PreservesMatrix()
    {
        float[] matrix = [1.5f, 0.0f, 0.0f, 1.5f, 100.0f, 200.0f];
        var original = new ObjectTransformedEvent("evt-3", "page-A", "obj-Z", "c1", FixedTime, matrix);

        var json = HubEnvelopeSerializer.SerializeEnvelope(HubEnvelopeSerializer.Wrap(original)!);
        var restored = HubEnvelopeSerializer.Unwrap(HubEnvelopeSerializer.DeserializeEnvelope(json)!)
            as ObjectTransformedEvent;

        Assert.NotNull(restored);
        Assert.Equal(matrix.Length, restored!.Matrix6.Length);
        for (int i = 0; i < matrix.Length; i++)
            Assert.Equal(matrix[i], restored.Matrix6[i], precision: 5);
    }

    [Fact]
    public void RoundTrip_ObjectsTransformedEvent_PreservesObjectIdsAndMatrix()
    {
        var original = new ObjectsTransformedEvent(
            "evt-batch", "page-A", "", "client-1", FixedTime,
            ["obj-X", "obj-Y"], [1f, 0f, 0f, 1f, 100f, 200f], "operation-1");

        var json = HubEnvelopeSerializer.SerializeEnvelope(HubEnvelopeSerializer.Wrap(original)!);
        var restored = HubEnvelopeSerializer.Unwrap(HubEnvelopeSerializer.DeserializeEnvelope(json)!)
            as ObjectsTransformedEvent;

        Assert.NotNull(restored);
        Assert.Equal(original.ObjectIds, restored!.ObjectIds);
        Assert.Equal(original.Matrix6, restored.Matrix6);
        Assert.Equal(original.OperationId, restored.OperationId);
    }

    [Fact]
    public void RoundTrip_ObjectAddedEvent_PreservesPayloadAndMetadata()
    {
        var original = new ObjectAddedEvent(
            "evt-added", "page-A", "obj-X", "client-1", FixedTime,
            [0xDE, 0xAD], "operation-1", "group-1");

        var json = HubEnvelopeSerializer.SerializeEnvelope(HubEnvelopeSerializer.Wrap(original)!);
        var restored = HubEnvelopeSerializer.Unwrap(HubEnvelopeSerializer.DeserializeEnvelope(json)!)
            as ObjectAddedEvent;

        Assert.NotNull(restored);
        Assert.Equal(original.ObjectData, restored!.ObjectData);
        Assert.Equal(original.OperationId, restored.OperationId);
        Assert.Equal(original.GroupId, restored.GroupId);
    }

    [Fact]
    public void RoundTrip_PageClearedEvent_PreservesOperationId()
    {
        var original = new PageClearedEvent("evt-cleared", "page-A", "", "client-1", FixedTime, "operation-2");

        var json = HubEnvelopeSerializer.SerializeEnvelope(HubEnvelopeSerializer.Wrap(original)!);
        var restored = HubEnvelopeSerializer.Unwrap(HubEnvelopeSerializer.DeserializeEnvelope(json)!)
            as PageClearedEvent;

        Assert.NotNull(restored);
        Assert.Equal(original.PageId, restored!.PageId);
        Assert.Equal(original.OperationId, restored.OperationId);
    }

    [Fact]
    public void RoundTrip_PageAddedEvent_PreservesPageIndex()
    {
        var original = new PageAddedEvent("evt-4", "page-NEW", "", "c1", FixedTime, 3);

        var json = HubEnvelopeSerializer.SerializeEnvelope(HubEnvelopeSerializer.Wrap(original)!);
        var restored = HubEnvelopeSerializer.Unwrap(HubEnvelopeSerializer.DeserializeEnvelope(json)!)
            as PageAddedEvent;

        Assert.NotNull(restored);
        Assert.Equal(3, restored!.PageIndex);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Unwrap_UnknownDiscriminator_ReturnsNull()
    {
        var env = new HubEnvelope("FutureEventType", "{}");
        Assert.Null(HubEnvelopeSerializer.Unwrap(env));
    }

    [Fact]
    public void Unwrap_MalformedPayload_ReturnsNull()
    {
        var env = new HubEnvelope("StrokeAdded", "not-valid-json{{{{");
        Assert.Null(HubEnvelopeSerializer.Unwrap(env));
    }

    [Fact]
    public void DeserializeEnvelope_InvalidJson_ReturnsNull()
        => Assert.Null(HubEnvelopeSerializer.DeserializeEnvelope("not json at all!!!"));

    [Fact]
    public void DeserializeEnvelope_EmptyString_ReturnsNull()
        => Assert.Null(HubEnvelopeSerializer.DeserializeEnvelope(string.Empty));

    [Fact]
    public void SerializeEnvelope_UsesCamelCase()
    {
        var env = new HubEnvelope("StrokeAdded", "{}");
        var json = HubEnvelopeSerializer.SerializeEnvelope(env);
        Assert.Contains("\"t\"", json);
        Assert.Contains("\"p\"", json);
        Assert.DoesNotContain("\"T\"", json);
        Assert.DoesNotContain("\"P\"", json);
    }

    [Fact]
    public void StrokeData_EmptyArray_RoundTrips()
    {
        var original = new StrokeAddedEvent("e-empty", "p1", "o1", "c1", FixedTime, []);

        var json = HubEnvelopeSerializer.SerializeEnvelope(HubEnvelopeSerializer.Wrap(original)!);
        var restored = HubEnvelopeSerializer.Unwrap(HubEnvelopeSerializer.DeserializeEnvelope(json)!)
            as StrokeAddedEvent;

        Assert.NotNull(restored);
        Assert.Empty(restored!.StrokeData);
    }
}

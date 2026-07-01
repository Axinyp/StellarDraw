namespace WhiteBoard.Protocol.Operations;

public record WhiteboardOperation(
    string OperationId,
    string ClientId,
    string PageId,
    string ObjectId,
    long BaseVersion,
    long? ServerSeqNo,
    OpType Type,
    byte[]? Payload
);
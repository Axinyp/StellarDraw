namespace WhiteBoard.UI.ViewModels;

/// <summary>本地图片插入的非敏感结果，供宿主提供用户可见反馈。</summary>
public sealed record ImageInsertResult(
    bool Succeeded,
    string Message,
    Guid? ObjectId = null,
    string? AssetId = null)
{
    public static ImageInsertResult Rejected(string message) => new(false, message);

    public static ImageInsertResult Failed(string message) => new(false, message);

    public static ImageInsertResult Inserted(Guid objectId, string assetId) =>
        new(true, "图片已插入。", objectId, assetId);
}

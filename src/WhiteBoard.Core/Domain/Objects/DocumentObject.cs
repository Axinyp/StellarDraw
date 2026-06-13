namespace WhiteBoard.Core.Domain.Objects;

/// <summary>
/// 嵌入文档对象：通过 <see cref="FileRef"/>（文件路径或云端引用）指向外部文档，
/// 记录总页数供翻页控件使用，渲染由宿主模块负责。
/// </summary>
public sealed class DocumentObject : WhiteboardObject
{
    public string FileRef   { get; }
    public int    PageCount { get; set; }

    public DocumentObject(string fileRef, int pageCount, float x, float y, float width, float height)
    {
        FileRef   = fileRef;
        PageCount = pageCount;
        SetBounds(x, y, width, height);
    }

    public DocumentObject(Guid id, string fileRef, int pageCount,
        float bx, float by, float bw, float bh, float[] transform)
        : base(id, bx, by, bw, bh, transform)
    {
        FileRef   = fileRef;
        PageCount = pageCount;
    }
}

namespace WhiteBoard.Core.Commands;

public interface IWhiteboardCommand
{
    IReadOnlyList<Guid> AffectedObjectIds { get; }

    /// <summary>
    /// 是否支持对单个对象的独立撤销。
    /// 批量命令（影响多个对象）应返回 false，禁止按对象撤销以防连带回滚。
    /// </summary>
    bool SupportsObjectUndo(Guid objectId);

    /// <summary>
    /// 若 SupportsObjectUndo = true，返回仅针对该对象的逆命令。
    /// </summary>
    IWhiteboardCommand? CreateObjectUndo(Guid objectId);

    void Execute();
    void Undo();
}

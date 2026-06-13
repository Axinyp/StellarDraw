namespace WhiteBoard.Core.Commands;

/// <summary>
/// 批量命令组：将多个命令合并为一个可整体撤销的原子单元。
/// AI 多步操作（如"绘制流程图"）应包裹在 CompositeCommand 中，保证 Ctrl+Z 一次撤销全部。
/// GroupId 供 AI undo 工具通过 groupId 参数引用。
/// </summary>
public sealed class CompositeCommand : IWhiteboardCommand
{
    private readonly IReadOnlyList<IWhiteboardCommand> _commands;

    public string GroupLabel { get; }
    public string GroupId    { get; }

    public CompositeCommand(IReadOnlyList<IWhiteboardCommand> commands,
        string groupLabel = "AI 操作", string? groupId = null)
    {
        _commands  = commands;
        GroupLabel = groupLabel;
        GroupId    = groupId ?? Guid.NewGuid().ToString("N");
    }

    public IReadOnlyList<Guid> AffectedObjectIds =>
        _commands.SelectMany(c => c.AffectedObjectIds).Distinct().ToList();

    // 批量命令不支持单对象粒度撤销，返回 false/null
    public bool                SupportsObjectUndo(Guid _) => false;
    public IWhiteboardCommand? CreateObjectUndo(Guid _)   => null;

    public void Execute()
    {
        foreach (var cmd in _commands)
            cmd.Execute();
    }

    public void Undo()
    {
        foreach (var cmd in _commands.Reverse())
            cmd.Undo();
    }
}

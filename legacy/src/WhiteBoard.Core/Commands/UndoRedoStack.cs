namespace WhiteBoard.Core.Commands;

/// <summary>
/// 命令历史栈：支持 Execute / Undo / Redo 的标准撤销重做机制，以及按对象 ID 的精细撤销（<see cref="UndoObject"/>）。
/// <para>
/// 容量上限：<see cref="_maxDepth"/> 条（默认 200）。超出时从栈底丢弃最旧的命令（O(n) 翻转，属于低频操作）。<br/>
/// 线程安全：不保证线程安全，所有调用须来自 Session 执行器线程（单消费者模型）。
/// </para>
/// </summary>
public sealed class UndoRedoStack
{
    private readonly int _maxDepth;
    private readonly Stack<IWhiteboardCommand> _undoStack = new();
    private readonly Stack<IWhiteboardCommand> _redoStack = new();

    public UndoRedoStack(int maxDepth = 200) => _maxDepth = maxDepth;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Execute(IWhiteboardCommand cmd)
    {
        cmd.Execute();
        _undoStack.Push(cmd);
        _redoStack.Clear();
        TrimToMaxDepth();
    }

    public void Undo()
    {
        if (_undoStack.TryPop(out var cmd))
        {
            cmd.Undo();
            _redoStack.Push(cmd);
        }
    }

    public void Redo()
    {
        if (_redoStack.TryPop(out var cmd))
        {
            cmd.Execute();
            _undoStack.Push(cmd);
        }
    }

    /// <summary>
    /// 撤销最近一条 SupportsObjectUndo=true 且影响到 objectId 的命令。
    /// 批量命令（SupportsObjectUndo=false）会被跳过，其位置被原样保留。
    /// temp 栈按原顺序还原，保证全局撤销栈顺序不变。
    /// </summary>
    public bool UndoObject(Guid objectId)
    {
        var temp = new Stack<IWhiteboardCommand>();

        while (_undoStack.TryPop(out var cmd))
        {
            if (cmd.SupportsObjectUndo(objectId))
            {
                var inverse = cmd.CreateObjectUndo(objectId);
                if (inverse != null)
                    inverse.Execute();  // execute inverse (e.g. Delete to undo an Add)
                else
                    cmd.Undo();         // fallback: standard undo on the original command

                // 命中命令进入 redo 栈，使该对象级撤销可被 Redo 重做；
                // 否则它会被永久丢弃（撤销后无法重做、栈状态与页面发散）。
                _redoStack.Push(cmd);

                // 按原顺序（从 temp 底部到顶部）放回 undoStack
                foreach (var t in temp.Reverse())
                    _undoStack.Push(t);

                return true;
            }
            temp.Push(cmd);
        }

        // 未找到：还原所有命令
        foreach (var t in temp.Reverse())
            _undoStack.Push(t);

        return false;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    private void TrimToMaxDepth()
    {
        while (_undoStack.Count > _maxDepth)
        {
            // Stack 不直接支持从底部删除，需要翻转
            var temp = new Stack<IWhiteboardCommand>(_undoStack);
            temp.Pop(); // 丢弃最旧的
            _undoStack.Clear();
            foreach (var c in temp)
                _undoStack.Push(c);
        }
    }
}
using WhiteBoard.Core.Commands;
using WhiteBoard.Core.Domain;
using WhiteBoard.Core.Domain.Objects;
using WhiteBoard.Contracts.Services;

namespace WhiteBoard.Core.Tests;

public class UndoRedoStackTests
{
    private static StrokeObject MakeStroke(Guid? id = null)
    {
        var pts   = new[] { new PressurePoint(0, 0, 0.5f, 0), new PressurePoint(10, 10, 0.5f, 10) };
        var style = new StrokeStyle(PenType.Pen, InkType.Normal, 0xFF000000, 2f, false, false);
        return new StrokeObject(id ?? Guid.NewGuid(), pts, style, 0, 0, 10, 10, new float[6]);
    }

    private static PageModel MakePage() => new("page1", "Test Page");

    [Fact]
    public void Execute_AddsToUndoStack_And_ClearsRedoStack()
    {
        var stack = new UndoRedoStack(maxDepth: 10);
        var page  = MakePage();
        var obj   = MakeStroke();

        var cmd1 = new AddObjectCommand(obj, page);
        stack.Execute(cmd1);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);

        // Simulate undo → now CanRedo
        stack.Undo();
        Assert.False(stack.CanUndo);
        Assert.True(stack.CanRedo);

        // Executing new command must clear redo
        var obj2 = MakeStroke();
        stack.Execute(new AddObjectCommand(obj2, page));
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Undo_Redo_RoundTrip_AddObject()
    {
        var stack = new UndoRedoStack();
        var page  = MakePage();
        var obj   = MakeStroke();

        stack.Execute(new AddObjectCommand(obj, page));
        Assert.Single(page.GetAllObjects());

        stack.Undo();
        Assert.Empty(page.GetAllObjects());

        stack.Redo();
        Assert.Single(page.GetAllObjects());
    }

    [Fact]
    public void Undo_Redo_RoundTrip_DeleteObject()
    {
        var stack = new UndoRedoStack();
        var page  = MakePage();
        var obj   = MakeStroke();

        page.AddObject(obj);
        stack.Execute(new DeleteObjectCommand(obj, page));
        Assert.Empty(page.GetAllObjects());

        stack.Undo();
        Assert.Single(page.GetAllObjects());

        stack.Redo();
        Assert.Empty(page.GetAllObjects());
    }

    [Fact]
    public void UndoObject_FindsAndUndoesSingleObjectCommand()
    {
        var stack = new UndoRedoStack();
        var page  = MakePage();

        var obj1 = MakeStroke();
        var obj2 = MakeStroke();

        stack.Execute(new AddObjectCommand(obj1, page));
        stack.Execute(new AddObjectCommand(obj2, page));

        Assert.Equal(2, page.GetAllObjects().Count);

        bool result = stack.UndoObject(obj2.Id);

        Assert.True(result);
        Assert.Single(page.GetAllObjects());
        // obj1 should still be in page
        Assert.Equal(obj1.Id, page.GetAllObjects()[0].Id);
    }

    [Fact]
    public void UndoObject_ReturnsFalse_WhenObjectNotInStack()
    {
        var stack = new UndoRedoStack();
        var page  = MakePage();
        var obj   = MakeStroke();

        stack.Execute(new AddObjectCommand(obj, page));

        bool result = stack.UndoObject(Guid.NewGuid()); // random id not in stack
        Assert.False(result);
    }

    [Fact]
    public void UndoObject_PreservesStackOrderForOtherCommands()
    {
        var stack = new UndoRedoStack();
        var page  = MakePage();

        var obj1 = MakeStroke();
        var obj2 = MakeStroke();
        var obj3 = MakeStroke();

        stack.Execute(new AddObjectCommand(obj1, page));
        stack.Execute(new AddObjectCommand(obj2, page));
        stack.Execute(new AddObjectCommand(obj3, page));

        // Undo obj2 specifically; obj1 and obj3 commands remain in order
        stack.UndoObject(obj2.Id);

        Assert.Equal(2, page.GetAllObjects().Count);

        // Undo again: should take obj3 (top of stack)
        stack.Undo();
        Assert.Single(page.GetAllObjects());
        Assert.Equal(obj1.Id, page.GetAllObjects()[0].Id);
    }

    [Fact]
    public void TrimToMaxDepth_RemovesOldestCommands()
    {
        var stack = new UndoRedoStack(maxDepth: 3);
        var page  = MakePage();

        for (int i = 0; i < 5; i++)
            stack.Execute(new AddObjectCommand(MakeStroke(), page));

        // Only 3 undos should be available
        int count = 0;
        while (stack.CanUndo) { stack.Undo(); count++; }
        Assert.Equal(3, count);
    }

    [Fact]
    public void Clear_EmptiesBothStacks()
    {
        var stack = new UndoRedoStack();
        var page  = MakePage();
        stack.Execute(new AddObjectCommand(MakeStroke(), page));
        stack.Undo();

        stack.Clear();

        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }
}

namespace WhiteBoard.AI.Tests;

/// <summary>
/// AgentLoopEvent 记录类型测试：确保事件携带正确的负载数据。
/// </summary>
public class AgentLoopEventTests
{
    [Fact]
    public void TextChunkEvent_CarriesChunk()
    {
        var evt = new TextChunkEvent("Hello");
        Assert.Equal("Hello", evt.Chunk);
    }

    [Fact]
    public void ToolCallingEvent_CarriesNameAndArgs()
    {
        var evt = new ToolCallingEvent("insert_shape", "{\"shapeType\":\"circle\"}");
        Assert.Equal("insert_shape", evt.ToolName);
        Assert.Contains("circle", evt.ArgsJson);
    }

    [Fact]
    public void ToolResultEvent_CarriesSuccessFlag()
    {
        var ok  = new ToolResultEvent("insert_shape", true,  "{}");
        var err = new ToolResultEvent("insert_shape", false, "{\"error\":\"fail\"}");

        Assert.True(ok.Success);
        Assert.False(err.Success);
    }

    [Fact]
    public void AgentErrorEvent_CarriesMessageAndRetryable()
    {
        var evt = new AgentErrorEvent("Connection refused", true);
        Assert.Equal("Connection refused", evt.Message);
        Assert.True(evt.IsRetryable);
    }

    [Fact]
    public void ConfirmRequiredEvent_InvokesConfirmCallback()
    {
        bool confirmed = false;
        var evt = new ConfirmRequiredEvent(
            "clear_page",
            "AI 将执行: clear_page",
            () => confirmed = true,
            () => { });

        evt.Confirm();
        Assert.True(confirmed);
    }

    [Fact]
    public void ConfirmRequiredEvent_InvokesCancelCallback()
    {
        bool cancelled = false;
        var evt = new ConfirmRequiredEvent(
            "clear_page",
            "AI 将执行: clear_page",
            () => { },
            () => cancelled = true);

        evt.Cancel();
        Assert.True(cancelled);
    }

    [Fact]
    public void Events_AreDistinguishableByType()
    {
        AgentLoopEvent text = new TextChunkEvent("hi");
        AgentLoopEvent tool = new ToolCallingEvent("undo", "{}");
        AgentLoopEvent err  = new AgentErrorEvent("oops", false);

        Assert.IsType<TextChunkEvent>(text);
        Assert.IsType<ToolCallingEvent>(tool);
        Assert.IsType<AgentErrorEvent>(err);
        Assert.IsNotType<TextChunkEvent>(tool);
    }
}

using WhiteBoard.Core.Domain.Objects;

namespace WhiteBoard.AI.Tests;

/// <summary>
/// WhiteboardToolRegistry 注册表测试。
/// 验证各 Catalog 工具名称的注册正确性。
/// </summary>
public class ToolRegistryTests
{
    // ── 辅助：构建最小 Mock ─────────────────────────────────────────────────────

    private static WhiteboardToolRegistry BuildRegistry(
        IWhiteboardAiActions? actions = null)
        => new(actions ?? new StubAiActions());

    // ── 工具数量与必要工具名称 ─────────────────────────────────────────────────────

    [Fact]
    public void Registry_ContainsAllBaseCatalogTools()
    {
        var registry = BuildRegistry();
        var tools    = registry.GetAvailableTools();

        // 5 个基础 Catalog 至少各贡献 1 个工具
        Assert.True(tools.Count >= 5,
            $"Expected ≥5 tools, got {tools.Count}");
    }

    [Theory]
    [InlineData("insert_shape")]     // DrawingToolCatalog
    [InlineData("insert_text")]      // ContentToolCatalog
    [InlineData("get_canvas_size")]  // QueryToolCatalog
    [InlineData("undo")]             // EditingToolCatalog
    [InlineData("add_page")]         // NavigationToolCatalog
    public void Registry_ContainsRequiredTool(string toolName)
    {
        var registry = BuildRegistry();
        var names    = registry.GetAvailableTools().Select(t => t.Name);
        Assert.Contains(toolName, names);
    }

    // ── 工具能力元数据 ─────────────────────────────────────────────────────────

    [Fact]
    public void ClearPage_IsDestructive()
    {
        var cap = BuildRegistry().GetCapability("clear_page");
        Assert.NotNull(cap);
        Assert.True(cap.IsDestructive);
        Assert.True(cap.RequiresConfirmation);
    }

    [Fact]
    public void GetCanvasSize_IsSafe()
    {
        var cap = BuildRegistry().GetCapability("get_canvas_size");
        Assert.NotNull(cap);
        Assert.False(cap.IsDestructive);
        Assert.False(cap.RequiresConfirmation);
        Assert.True(cap.SafeInCollaborationMode);
    }

    // ── 外部工具注册 ──────────────────────────────────────────────────────────

    [Fact]
    public void ExternalTools_AreRegisteredAndFlagged()
    {
        var registry = BuildRegistry();
        var fn = Microsoft.Extensions.AI.AIFunctionFactory.Create(
            (string input) => $"echo:{input}", "ext_echo", "Echo tool");

        registry.RegisterExternalTools([(fn, false, false)]);

        var names = registry.GetAvailableTools().Select(t => t.Name).ToList();
        Assert.Contains("ext_echo", names);
        Assert.True(registry.IsExternalTool("ext_echo"));
    }

    [Fact]
    public void GetFunctions_CountMatchesAvailableTools()
    {
        var registry = BuildRegistry();
        Assert.Equal(
            registry.GetAvailableTools().Count,
            registry.GetFunctions().Count);
    }

    // ── get_canvas_size 实际调用 ──────────────────────────────────────────────

    [Fact]
    public async Task GetCanvasSize_Tool_ReturnsExpectedDimensions()
    {
        var stub     = new StubAiActions { CanvasWidth = 1920f, CanvasHeight = 1080f };
        var registry = BuildRegistry(stub);

        var tools = registry.GetAvailableTools();
        var tool  = tools.FirstOrDefault(t => t.Name == "get_canvas_size");
        Assert.NotNull(tool);

        // 直接调用工具（通过 InvokeAsync）
        var result = await registry.InvokeAsync("get_canvas_size", null, CancellationToken.None);
        var json   = result?.ToString() ?? "";

        Assert.Contains("1920", json);
        Assert.Contains("1080", json);
    }
}

// ── Stub 实现 ────────────────────────────────────────────────────────────────

file sealed class StubAiActions : IWhiteboardAiActions
{
    public float CanvasWidth  { get; set; } = 800f;
    public float CanvasHeight { get; set; } = 600f;

    public (float Width, float Height) GetCanvasSize() => (CanvasWidth, CanvasHeight);

    public IReadOnlyDictionary<string, Guid> GetAliasMap() => new Dictionary<string, Guid>();
    public void RegisterAlias(string alias, Guid objectId) { }
    public void ClearAliases() { }

    public Task<ToolResult> InsertShapeAsync(ShapeSpec spec, string? alias = null,
        string? groupId = null, CancellationToken ct = default)
        => Task.FromResult(ToolResult.Ok());

    public Task<ToolResult> InsertPathAsync(PathSpec spec, string? alias = null,
        string? groupId = null, CancellationToken ct = default)
        => Task.FromResult(ToolResult.Ok());

    public Task<ToolResult> InsertTextAsync(TextSpec spec, string? alias = null,
        string? groupId = null, CancellationToken ct = default)
        => Task.FromResult(ToolResult.Ok());

    public Task<ToolResult> InsertImageAsync(ImageSpec spec, string? alias = null,
        string? groupId = null, CancellationToken ct = default)
        => Task.FromResult(ToolResult.Ok());

    public Task<ToolResult> MoveObjectsAsync(IReadOnlyList<string> aliasesOrIds,
        float dx, float dy, string? groupId = null, CancellationToken ct = default)
        => Task.FromResult(ToolResult.Ok());

    public Task<ToolResult> DeleteObjectsAsync(IReadOnlyList<string> aliasesOrIds,
        string? groupId = null, CancellationToken ct = default)
        => Task.FromResult(ToolResult.Ok());

    public Task<ToolResult> ClearPageAsync(CancellationToken ct = default)
        => Task.FromResult(ToolResult.Ok());

    public Task<ToolResult> UndoAsync(string? groupId = null, CancellationToken ct = default)
        => Task.FromResult(ToolResult.Ok());

    public Task<ToolResult> RedoAsync(CancellationToken ct = default)
        => Task.FromResult(ToolResult.Ok());

    public Task<ToolResult> SelectObjectsAsync(float x, float y, float w, float h,
        CancellationToken ct = default)
        => Task.FromResult(ToolResult.Ok());

    public Task<PageSnapshot> GetSnapshotAsync(CancellationToken ct = default)
        => Task.FromResult(new PageSnapshot { PageId = "page1", Version = 1, Width = 800, Height = 600 });

    public Task<ToolResult> AddPageAsync(string? title = null, CancellationToken ct = default)
        => Task.FromResult(ToolResult.Ok());

    public Task<ToolResult> NavigateToPageAsync(int pageIndex, CancellationToken ct = default)
        => Task.FromResult(ToolResult.Ok());

    public Task<string> GetPagesInfoAsync(CancellationToken ct = default)
        => Task.FromResult("{\"count\":1,\"current\":0}");
}

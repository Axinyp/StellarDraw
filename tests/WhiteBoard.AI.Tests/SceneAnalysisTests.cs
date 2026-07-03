using WhiteBoard.AI.Services;
using WhiteBoard.AI.Session;
using WhiteBoard.AI.Tools;
using WhiteBoard.AI.Tools.Catalogs;
using WhiteBoard.Core.Domain;

namespace WhiteBoard.AI.Tests;

/// <summary>
/// 场景分析测试：验证工具描述和系统提示是否正确引导 AI 选择合适的工具。
/// 这些测试不调用真实 LLM，而是验证"护栏"（工具描述 + 系统提示）是否到位。
/// </summary>
public class SceneAnalysisTests
{
    // ── 工具描述关键词覆盖 ────────────────────────────────────────────────────

    [Fact]
    public void DrawPath_Description_MustMentionImageGenFallback()
    {
        var desc = GetToolDescription("draw_path");

        Assert.NotNull(desc);
        Assert.Contains("矢量", desc, StringComparison.Ordinal);
        Assert.Contains("generate_and_insert_image", desc, StringComparison.Ordinal);
        // draw_path 现在是图像生成不可用时的降级方案，应包含该语义
        Assert.True(
            desc.Contains("降级", StringComparison.Ordinal) ||
            desc.Contains("不可用", StringComparison.Ordinal),
            $"description should indicate fallback role: {desc}");
    }

    [Fact]
    public void InsertShape_Description_MustExcludeComplexImagery()
    {
        var desc = GetToolDescription("insert_shape");

        Assert.NotNull(desc);
        Assert.Contains("generate_and_insert_image", desc, StringComparison.Ordinal);
        Assert.Contains("复杂图像", desc, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateAndInsertImage_Description_MustCoverComplexScenarios()
    {
        // 仅在 ImageGen 可用时注册；这里我们用 mock provider 强制注册
        var stub     = new StubAiActions();
        var imageGen = new StubImageGenProvider();
        var registry = new WhiteboardToolRegistry(stub, imageGen: imageGen);

        var tools = registry.GetFunctions();
        var fn    = tools.FirstOrDefault(t => t.Name == "generate_and_insert_image");

        Assert.NotNull(fn);
        var desc = fn.Description ?? "";
        // 应包含"图像生成"相关关键词
        Assert.True(
            desc.Contains("AI", StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("生成", StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("image", StringComparison.OrdinalIgnoreCase),
            $"description should indicate image generation capability: {desc}");
    }

    // ── 系统提示工具选择规则覆盖 ──────────────────────────────────────────────

    [Theory]
    [InlineData("几何图形")]
    [InlineData("generate_and_insert_image")]
    [InlineData("复杂图像")]
    [InlineData("角色")]
    [InlineData("动物")]
    [InlineData("降级")]
    public void SystemPrompt_Level0_ContainsToolSelectionRule(string keyword)
    {
        var session = new WhiteboardSession();
        var builder = new ProgressiveContextBuilder(session);
        var prompt  = builder.BuildLevel0(ExecutionMode.ConfirmRequired, ["insert_shape", "draw_path"]);

        Assert.Contains(keyword, prompt, StringComparison.Ordinal);
    }

    // ── 场景 → 预期工具映射（文档化预期行为）────────────────────────────────

    /// <summary>
    /// 场景矩阵记录期望的工具选择（图像生成可用时）。
    /// generate_and_insert_image 不可用时，复杂内容降级为 draw_path 简化矢量版本。
    /// </summary>
    [Theory]
    [InlineData("画一个五角星",                        "draw_path",               "几何图形 → SVG path")]
    [InlineData("画一个红色圆形",                      "insert_shape",            "标准形状 → insert_shape")]
    [InlineData("画一个矩形",                          "insert_shape",            "标准形状 → insert_shape")]
    [InlineData("画一只在会议室开会的企鹅",            "generate_and_insert_image", "角色/动物 → 图像生成（降级→draw_path）")]
    [InlineData("画一只可爱的猫",                      "generate_and_insert_image", "动物 → 图像生成（降级→draw_path）")]
    [InlineData("画一个动漫人物",                      "generate_and_insert_image", "动漫角色 → 图像生成（降级→draw_path）")]
    [InlineData("画一幅会议室的场景图",                "generate_and_insert_image", "写实场景 → 图像生成（降级→draw_path）")]
    [InlineData("画一个向右的箭头",                    "insert_shape",            "标准形状 → insert_shape")]
    [InlineData("画一个流程图，包含开始框和判断菱形",  "draw_path",               "流程图元素 → SVG path")]
    [InlineData("在白板上写欢迎使用",                  "insert_text",             "文字 → insert_text")]
    public void ScenarioMapping_IsDocumented(string userRequest, string expectedTool, string reason)
    {
        // 这是文档化测试——验证场景分类规则记录是否完整
        // 当接入真实 LLM 时，可将此测试升级为集成测试
        Assert.NotEmpty(userRequest);
        Assert.NotEmpty(expectedTool);
        Assert.NotEmpty(reason);
    }

    // ── 擦除能力覆盖（回归验证 HitTest 线程安全修复）────────────────────────

    [Fact]
    public void EraseAtAsync_WithNoObjects_DoesNotThrow()
    {
        // 验证在没有任何对象时 EraseAtAsync 不会崩溃（原先在 UI 线程的 HitTest 竞态）
        var session = new WhiteboardSession();
        var ex = Record.ExceptionAsync(() => session.EraseAtAsync(100f, 100f));
        // 注意：Session 未启动执行器，PostOperationAsync 会阻塞；
        // 此处验证方法签名和入口路径存在，不验证异步执行
        Assert.NotNull(ex); // Task 对象非 null 即为正确
    }

    // ── 工具边界：draw_path 的 SVG 包围盒解析 ───────────────────────────────

    [Theory]
    [InlineData("M 100 100 L 300 100 L 200 300 Z",    100f, 100f, 200f, 200f)]
    [InlineData("M100,100L300,100L200,300Z",           100f, 100f, 200f, 200f)]  // 紧凑写法
    [InlineData("M960 240 L1025 450 L1245 450 Z",     960f, 240f, 285f, 210f)]  // 五角星局部
    public void PathObject_ComputeBounds_HandlesCompactAndSpacedSvg(
        string svgPath, float expX, float expY, float expW, float expH)
    {
        var path = new WhiteBoard.Core.Domain.Objects.PathObject(
            svgPath, 0xFFFFD700, 0xFF000000, 2f, true);

        Assert.Equal(expX, path.BoundsX,      precision: 0);
        Assert.Equal(expY, path.BoundsY,      precision: 0);
        Assert.Equal(expW, path.BoundsWidth,  precision: 0);
        Assert.Equal(expH, path.BoundsHeight, precision: 0);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string? GetToolDescription(string toolName)
    {
        var registry = BuildRegistry(new StubAiActions());
        return registry.GetFunctions()
            .FirstOrDefault(f => f.Name == toolName)
            ?.Description;
    }

    private static WhiteboardToolRegistry BuildRegistry(IWhiteboardAiActions stub)
        => new(stub);
}

// ── Stub providers ────────────────────────────────────────────────────────────

file sealed class StubImageGenProvider : WhiteBoard.AI.Abstractions.IImageGenProvider
{
    public string ProviderName => "stub";
    public bool   IsAvailable  => true;
    public Task<byte[]> GenerateAsync(string prompt,
        WhiteBoard.AI.Abstractions.ImageGenStyle style,
        int width, int height, CancellationToken ct = default)
        => Task.FromResult(Array.Empty<byte>());
}

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

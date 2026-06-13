using Microsoft.Extensions.AI;
using WhiteBoard.AI.Abstractions;
using WhiteBoard.AI.Services;
using WhiteBoard.AI.Tools.Catalogs;

namespace WhiteBoard.AI.Tools;

/// <summary>
/// 白板工具统一注册表。
/// 按能力条件过滤（ImageGen/Math 工具仅在对应 Provider 可用时注册）。
/// AgentLoop 通过 GetAvailableTools() 获取当前可用工具列表传给 LLM。
/// </summary>
public sealed class WhiteboardToolRegistry
{
    private readonly Dictionary<string, AIFunction>        _functions    = new();
    private readonly Dictionary<string, ToolCapabilityInfo> _capabilities = new();
    private readonly HashSet<string>                       _externalNames = [];

    public WhiteboardToolRegistry(
        IWhiteboardAiActions actions,
        IImageGenProvider?   imageGen     = null,
        IMathRenderer?       mathRenderer = null)
    {
        DrawingToolCatalog.Register(this, actions);
        EditingToolCatalog.Register(this, actions);
        QueryToolCatalog.Register(this, actions);
        ContentToolCatalog.Register(this, actions);
        NavigationToolCatalog.Register(this, actions);

        if (imageGen?.IsAvailable == true)
            RegisterImageGenTools(actions, imageGen);

        if (mathRenderer?.IsAvailable == true)
            RegisterMathTools(actions, mathRenderer);
    }

    public void Register(AIFunction fn, ToolCapabilityInfo capability)
    {
        _functions[fn.Name]    = fn;
        _capabilities[fn.Name] = capability;
    }

    public IReadOnlyList<AITool> GetAvailableTools() => [.. _functions.Values];

    /// <summary>Returns all registered AIFunctions (used by McpToolBridge for export).</summary>
    public IReadOnlyList<AIFunction> GetFunctions() => [.. _functions.Values];

    public bool IsExternalTool(string name) => _externalNames.Contains(name);

    public ToolCapabilityInfo? GetCapability(string name) =>
        _capabilities.GetValueOrDefault(name);

    public async Task<object?> InvokeAsync(string name,
        IDictionary<string, object?>? args, CancellationToken ct)
    {
        if (!_functions.TryGetValue(name, out var fn))
            throw new InvalidOperationException($"Unknown tool: {name}");

        AIFunctionArguments? fnArgs = null;
        if (args is not null)
        {
            fnArgs = new AIFunctionArguments();
            foreach (var (k, v) in args)
                if (v is not null) fnArgs.Add(k, v);
        }

        return await fn.InvokeAsync(fnArgs, ct);
    }

    private void RegisterImageGenTools(IWhiteboardAiActions actions,
        IImageGenProvider imageGen)
        => ImageGenToolCatalog.Register(this, actions, imageGen);

    private void RegisterMathTools(IWhiteboardAiActions actions,
        IMathRenderer mathRenderer)
        => MathToolCatalog.Register(this, actions, mathRenderer);

    /// <summary>
    /// 注册视口控制工具（zoom_in / zoom_out / reset_view）。
    /// 由 UI 层在完成 InkSurface 初始化后调用，传入实际操作视口的回调。
    /// </summary>
    public void RegisterViewportControls(
        Action? zoomIn    = null,
        Action? zoomOut   = null,
        Action? resetView = null)
        => NavigationToolCatalog.RegisterViewportControls(this, zoomIn, zoomOut, resetView);

    /// <summary>
    /// Registers external MCP tools obtained from McpClientProvider.
    /// Prefixed names prevent collisions with built-in tools.
    /// </summary>
    public void RegisterExternalTools(
        IReadOnlyList<(AIFunction function, bool requiresConfirmation, bool isDestructive)> registrations)
    {
        foreach (var (fn, requiresConfirmation, isDestructive) in registrations)
        {
            _functions[fn.Name]    = fn;
            _capabilities[fn.Name] = new ToolCapabilityInfo(
                IsDestructive:        isDestructive,
                RequiresConfirmation: requiresConfirmation,
                RequiredProviders:    [],
                SupportsStreaming:    false,
                SafeInCollaborationMode: !isDestructive);
            _externalNames.Add(fn.Name);
        }
    }
}

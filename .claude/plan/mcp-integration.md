# MCP 集成实施计划
> 生成时间：2026-06-12
> Codex SESSION_ID: 019eba63-fbe8-73e3-bec9-67d474ed5eec
> 前置计划：ai-whiteboard-design.md（Phases A-G 已完成）

---

## 架构总览

```
外部 AI 客户端 (Claude Desktop / Cursor)
        ↕ stdio (WHITEBOARD_MCP_TOKEN 环境变量)
  WhiteBoard.McpHost.exe （独立控制台进程）
        ↕ Named Pipe "whiteboard-mcp"
  WPF 主进程 (WhiteboardMcpPipeServer)
        ↕ 直接引用
  McpSessionRegistry → WhiteboardAISession
        ↕ 直接引用
  WhiteboardToolRegistry (AIFunction 注册表)

远程 AI 客户端 (Web / API 调用方)
        ↕ HTTP/SSE + Authorization: Bearer <token>
  EmbeddedMcpServerHost (Kestrel 内嵌)
        ↕ 直接引用 (in-process)
  McpSessionRegistry → WhiteboardAISession
```

**关键 SDK 发现（Codex 验证）**：
- `McpClientTool` 已继承 `AIFunction` → 外部 MCP 工具零适配直接入注册表
- `McpServerTool.Create(AIFunction, ...)` → 现有工具零重写直接暴露
- HTTP 传输支持 per-session 工具集注入（`ConfigureSessionOptions`）

---

## 任务类型：全栈（后端主导）

---

## 项目结构

### 新增项目

```
src/
├── WhiteBoard.Mcp.Core/          ← 共享适配层（不含传输实现）
│   ├── Registry/
│   │   ├── IMcpSessionRegistry.cs
│   │   ├── McpSessionRegistry.cs
│   │   ├── McpTokenDescriptor.cs
│   │   └── McpSessionBinding.cs
│   ├── Server/
│   │   ├── IMcpToolBridge.cs
│   │   ├── McpToolBridge.cs       ← AIFunction ↔ McpServerTool
│   │   └── McpExportOptions.cs
│   ├── IPC/
│   │   ├── PipeContract.cs        ← 管道消息契约
│   │   ├── WhiteboardMcpPipeServer.cs  ← WPF 侧管道服务端
│   │   └── WhiteboardMcpPipeClient.cs  ← McpHost 侧管道客户端
│   ├── Hosting/
│   │   ├── EmbeddedMcpServerHost.cs    ← Kestrel 内嵌主机
│   │   └── McpTokenAuthHandler.cs      ← Bearer Token 认证中间件
│   └── Client/
│       ├── IMcpClientProvider.cs
│       ├── McpClientProvider.cs         ← 外部 MCP 服务发现 + 导入
│       ├── ExternalMcpConnectionSpec.cs
│       └── ExternalMcpToolRegistration.cs
│
└── WhiteBoard.McpHost/           ← stdio 控制台进程（Claude Desktop 启动）
    ├── Program.cs                ← stdio MCP 主机 + 命名管道代理
    └── McpPipeBackedTool.cs      ← 将管道调用包装为 McpServerTool
```

---

## 实施阶段

### Phase H：MCP Server（优先级：高）

#### H1：WhiteBoard.Mcp.Core 项目骨架

**新建文件**：`WhiteBoard.Mcp.Core.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\WhiteBoard.AI\WhiteBoard.AI.csproj" />
    <PackageReference Include="ModelContextProtocol"          Version="0.3.0-preview.1" />
    <PackageReference Include="ModelContextProtocol.AspNetCore" Version="0.3.0-preview.1" />
    <PackageReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
</Project>
```

**`IMcpSessionRegistry.cs`**（关键接口）：
```csharp
public interface IMcpSessionRegistry
{
    Task<McpTokenDescriptor> CreateTokenAsync(
        WhiteboardAISession session,
        McpTokenPolicy      policy,
        CancellationToken   ct = default);

    Task<McpSessionBinding?> ResolveAsync(
        string token, string transport, CancellationToken ct = default);

    Task RevokeAsync(string token, CancellationToken ct = default);

    IReadOnlyList<McpSessionBinding> ActiveBindings { get; }
    event EventHandler<McpSessionBinding> ConnectionOpened;
    event EventHandler<McpSessionBinding> ConnectionClosed;
}

public sealed record McpTokenDescriptor(
    string  Token,
    string  TokenHash,       // SHA-256，日志/审计用
    DateTimeOffset ExpiresAt,
    McpTokenPolicy Policy);

public sealed record McpTokenPolicy(
    TimeSpan Lifetime,
    bool     AllowDestructiveTools,
    IReadOnlySet<string> AllowedTransports);  // {"stdio","http","sse"}

public sealed class McpSessionBinding
{
    public string              Token         { get; init; } = "";
    public WeakReference<WhiteboardAISession> SessionRef { get; init; } = null!;
    public McpTokenPolicy      Policy        { get; init; } = null!;
    public DateTimeOffset      BoundAt       { get; init; }
    public DateTimeOffset      ExpiresAt     { get; init; }
    public string              Transport     { get; set; } = "";
    public DateTimeOffset?     LastUsedAt    { get; set; }
    public bool                IsRevoked     { get; set; }
}
```

**`McpSessionRegistry.cs`** 实现要点：
- 使用 `ConcurrentDictionary<string, McpSessionBinding>` 存储
- 后台 Timer 每分钟清理过期 Token
- `ResolveAsync` 验证：token 存在 + 未撤销 + 未过期 + transport 在白名单 + session 弱引用仍存活

#### H2：McpToolBridge（AIFunction ↔ McpServerTool）

**`McpToolBridge.cs`** 核心逻辑：
```csharp
public sealed class McpToolBridge : IMcpToolBridge
{
    public IReadOnlyList<McpServerTool> ExportTools(
        WhiteboardToolRegistry registry,
        McpExportOptions       options)
    {
        var result = new List<McpServerTool>();
        foreach (var fn in registry.GetFunctions())
        {
            var cap = registry.GetCapability(fn.Name);
            // 根据 options.IncludeDestructiveTools 过滤
            if (cap?.IsDestructive == true && !options.IncludeDestructiveTools)
                continue;
            // McpServerTool.Create 直接包装 AIFunction
            var tool = McpServerTool.Create(fn,
                new McpServerToolCreateOptions
                {
                    Name        = options.PrefixToolNames
                                  ? $"{options.ToolNamePrefix}__{fn.Name}"
                                  : fn.Name,
                    Description = fn.Description,
                });
            result.Add(tool);
        }
        return result;
    }
}

public sealed record McpExportOptions(
    bool   IncludeDestructiveTools = false,
    bool   PrefixToolNames         = false,
    string ToolNamePrefix          = "wb");
```

#### H3：Named Pipe IPC（stdio 路径）

**设计思路**：Claude Desktop 启动 `WhiteBoard.McpHost.exe` → McpHost 通过命名管道调用 WPF 进程中的实际 WhiteboardAISession。

**`PipeContract.cs`** 消息结构：
```csharp
// 所有消息使用 System.Text.Json 序列化，换行分隔（NDJSON）
public abstract record PipeMessage(string Type);

public record PipeListToolsRequest(string Token)      : PipeMessage("list_tools");
public record PipeListToolsResponse(
    IReadOnlyList<PipeToolInfo> Tools)                : PipeMessage("list_tools_result");

public record PipeInvokeToolRequest(
    string Token, string Name,
    IDictionary<string, object?> Args)               : PipeMessage("invoke_tool");
public record PipeInvokeToolResponse(
    bool Success, string Json,
    string? Error)                                    : PipeMessage("invoke_tool_result");

public record PipeToolInfo(
    string Name, string Description,
    bool IsDestructive, string JsonSchema);
```

**`WhiteboardMcpPipeServer.cs`**（WPF 进程侧）：
- 使用 `NamedPipeServerStream`，每个连接一个后台 Task
- 管道名：`whiteboard-mcp-{pid}` 或固定 `whiteboard-mcp`
- 收到 `list_tools`：从 token 解析 binding → 调用 `McpToolBridge.ExportTools`
- 收到 `invoke_tool`：调用 `WhiteboardToolRegistry.InvokeAsync`

**`WhiteBoard.McpHost/Program.cs`** 结构：
```csharp
// 读取环境变量
var token    = Environment.GetEnvironmentVariable("WHITEBOARD_MCP_TOKEN") ?? "";
var pipeName = Environment.GetEnvironmentVariable("WHITEBOARD_MCP_PIPE_NAME") ?? "whiteboard-mcp";

var pipeClient = new WhiteboardMcpPipeClient(pipeName);
await pipeClient.ConnectAsync();

// 从管道获取工具列表
var toolList = await pipeClient.ListToolsAsync(new(token));

// 将管道工具包装为 McpServerTool
var mcpTools = toolList.Tools.Select(t =>
    McpServerTool.Create(
        BuildPipeBackedDelegate(pipeClient, token, t),
        new McpServerToolCreateOptions { Name = t.Name, Description = t.Description }));

// 启动 stdio MCP server
await McpServerFactory.Create(mcpTools)
    .RunAsStdioAsync();
```

#### H4：EmbeddedMcpServerHost（HTTP/SSE 路径）

**`EmbeddedMcpServerHost.cs`** 使用 `ModelContextProtocol.AspNetCore`：
```csharp
public sealed class EmbeddedMcpServerHost : IAsyncDisposable
{
    private WebApplication? _app;

    public async ValueTask StartAsync(
        EmbeddedMcpServerOptions opts,
        IMcpSessionRegistry registry,
        IMcpToolBridge bridge,
        CancellationToken ct = default)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(opts.Url);

        builder.Services
            .AddSingleton(registry)
            .AddSingleton(bridge)
            .AddAuthentication("McpToken")
            .AddScheme<McpTokenAuthOptions, McpTokenAuthHandler>("McpToken", _ => { });

        builder.Services
            .AddMcpServer()
            .WithHttpTransport(o =>
            {
                o.EnableLegacySse = opts.EnableLegacySse;
                o.ConfigureSessionOptions = async (ctx, mcpOpts, ct) =>
                {
                    var token   = ExtractBearerToken(ctx);
                    var binding = await registry.ResolveAsync(token, "http", ct)
                                  ?? throw new UnauthorizedAccessException();
                    if (!binding.SessionRef.TryGetTarget(out var session))
                        throw new InvalidOperationException("Session expired");

                    var tools = bridge.ExportTools(
                        session.Registry,
                        new(binding.Policy.AllowDestructiveTools));
                    mcpOpts.ToolCollection = new McpToolCollection(tools);
                };
            });

        _app = builder.Build();
        _app.UseAuthentication();
        _app.MapMcp(opts.RoutePrefix).RequireAuthorization();
        await _app.StartAsync(ct);
    }
}
```

#### H5：AIWhiteboardSettings 扩展

**修改文件**：`src/WhiteBoard.AI/Settings/AIWhiteboardSettings.cs`

新增：
```csharp
[JsonPropertyName("mcpServer")]
public McpServerSettings McpServer { get; set; } = new();

[JsonPropertyName("mcpClient")]
public McpClientSettings McpClient { get; set; } = new();
```

```csharp
public sealed class McpServerSettings
{
    public bool   Enabled                { get; set; } = false;
    public bool   StdioEnabled           { get; set; } = true;
    public bool   HttpEnabled            { get; set; } = true;
    public bool   EnableLegacySse        { get; set; } = false;   // 仅兼容，默认关
    public string HttpUrl                { get; set; } = "http://127.0.0.1:31888";
    public string RoutePrefix            { get; set; } = "/mcp";
    public int    TokenLifetimeMinutes   { get; set; } = 60;
    public bool   AllowDestructiveTools  { get; set; } = false;
    public List<string> AllowedHosts     { get; set; } = ["127.0.0.1", "localhost"];
}

public sealed class McpClientSettings
{
    public bool Enabled { get; set; } = false;
    public List<McpClientConnectionSettings> Connections { get; set; } = [];
}

public sealed class McpClientConnectionSettings
{
    public string ConnectionId      { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName       { get; set; } = "";
    public bool   Enabled           { get; set; } = true;
    public string Transport         { get; set; } = "stdio";   // stdio | http
    public string Command           { get; set; } = "";        // stdio 模式：可执行文件路径
    public List<string> Arguments   { get; set; } = [];
    public string WorkingDirectory  { get; set; } = "";
    public string Endpoint          { get; set; } = "";        // http 模式：URL
    public string ApiKeyRef         { get; set; } = "";        // Credential Manager 键名
    public string ToolNamePrefix    { get; set; } = "ext";
    public string TrustLevel        { get; set; } = "restricted";  // restricted | trusted
    public bool   AllowDestructiveTools { get; set; } = false;
}
```

#### H6：WhiteboardAISession 异步初始化

**修改文件**：`src/WhiteBoard.AI/Session/WhiteboardAISession.cs`

新增接口：
```csharp
// 必须在构造后、第一次 SendMessageAsync 前调用
public async ValueTask InitializeAsync(
    AIWhiteboardSettings settings,
    IMcpClientProvider?  mcpClientProvider = null,
    EmbeddedMcpServerHost? httpHost = null,
    CancellationToken    ct = default)

// 生成并注册 Token，触发 McpTokenIssued 事件供 UI 显示
public async ValueTask<McpTokenDescriptor?> IssueMcpTokenAsync(
    AIWhiteboardSettings settings,
    CancellationToken    ct = default)

// UI 订阅此事件更新 Token 显示
public event EventHandler<McpTokenDescriptor>? McpTokenIssued;
```

#### H7：UI 更新（MCP Server 面板）

**修改文件**：`src/WhiteBoard.UI/ViewModels/AiSettingsViewModel.cs`

新增 ViewModel 属性：
- `McpServerEnabled`、`McpServerStdioEnabled`、`McpServerHttpEnabled`、`McpServerSseEnabled`
- `McpServerHttpUrl`、`McpServerRoutePrefix`
- `McpTokenLifetimeMinutes`、`McpAllowDestructiveTools`
- `CurrentMcpToken`（只读，从 `WhiteboardAISession.McpTokenIssued` 更新）
- `ClaudeDesktopConfigSnippet`（computed，自动生成配置示例）

```csharp
// 自动生成 Claude Desktop 配置片段
public string ClaudeDesktopConfigSnippet =>
    $$"""
    {
      "mcpServers": {
        "whiteboard": {
          "command": "WhiteBoard.McpHost.exe",
          "env": {
            "WHITEBOARD_MCP_TOKEN": "{{CurrentMcpToken}}",
            "WHITEBOARD_MCP_PIPE_NAME": "whiteboard-mcp"
          }
        }
      }
    }
    """;
```

新增命令：
- `RegenerateMcpTokenCommand` — 重新生成 Token
- `CopyMcpTokenCommand` — 复制到剪贴板
- `CopyClaudeConfigCommand` — 复制完整配置

**修改文件**：`src/WhiteBoard.UI/ViewModels/AiChatViewModel.cs`

新增 MCP 活动指示器：
```csharp
public int  McpActiveConnections { get; private set; }   // 当前连接数
public bool HasMcpActivity       { get; private set; }   // 外部客户端正在调用工具
```

---

### Phase I：MCP Client（优先级：中）

#### I1：McpClientProvider（外部 MCP 服务导入）

**`McpClientProvider.cs`** 核心逻辑：
```csharp
public sealed class McpClientProvider : IMcpClientProvider, IAsyncDisposable
{
    private readonly List<(IMcpClient client, ExternalMcpConnectionSpec spec)> _clients = [];

    public async ValueTask InitializeAsync(
        IReadOnlyList<ExternalMcpConnectionSpec> connections,
        CancellationToken ct = default)
    {
        foreach (var conn in connections.Where(c => c.Enabled))
        {
            try
            {
                var transport = conn.Transport switch
                {
                    "stdio" => (IMcpTransport)new StdioClientTransport(
                                   new StdioServerParameters
                                   {
                                       Command          = conn.Command!,
                                       Arguments        = conn.Arguments,
                                       WorkingDirectory = conn.WorkingDirectory,
                                   }),
                    "http"  => new SseClientTransport(
                                   new SseClientTransportOptions { Endpoint = new Uri(conn.Endpoint!) }),
                    _       => throw new NotSupportedException(conn.Transport)
                };
                var client = await McpClientFactory.CreateAsync(transport, ct: ct);
                _clients.Add((client, conn));
            }
            catch (Exception ex)
            {
                // 记录错误但不中断其他连接初始化
                Debug.WriteLine($"MCP client {conn.DisplayName} init failed: {ex.Message}");
            }
        }
    }

    public async ValueTask<IReadOnlyList<ExternalMcpToolRegistration>> GetToolRegistrationsAsync(
        CancellationToken ct = default)
    {
        var result = new List<ExternalMcpToolRegistration>();
        foreach (var (client, spec) in _clients)
        {
            var tools = await client.ListToolsAsync(ct: ct);
            foreach (var tool in tools)   // tool is McpClientTool : AIFunction
            {
                var prefixed = tool.WithName($"{spec.ToolNamePrefix}__{tool.Name}");
                var policy = new ExternalMcpToolPolicy(
                    RequiresConfirmation: spec.TrustLevel == "restricted",
                    IsDestructive:        false,   // 保守默认
                    AllowInAutoApproveMode: spec.TrustLevel == "trusted");
                result.Add(new(spec.ConnectionId, tool.Name, prefixed.Name, prefixed, policy));
            }
        }
        return result;
    }
}
```

#### I2：WhiteboardToolRegistry 动态注册

**修改文件**：`src/WhiteBoard.AI/Tools/WhiteboardToolRegistry.cs`

新增：
```csharp
// 动态注册外部 MCP 工具（WhiteboardAISession.InitializeAsync 调用）
public void RegisterExternalTools(
    IReadOnlyList<ExternalMcpToolRegistration> registrations)

// 供 McpToolBridge 导出所有函数声明
public IReadOnlyList<AIFunction> GetFunctions()

// 供日志和 UI 区分本地工具 vs 外部工具
public bool IsExternalTool(string name)
```

#### I3：工具结果归一化

**修改文件**：`src/WhiteBoard.AI/Session/AgentLoop.cs`

现有：`toolResult?.ToString()` → 改为通过 `ToolResultEnvelope.Normalize(toolResult)` 统一处理，确保本地工具返回的 JSON 字符串和外部 MCP 工具返回的 `CallToolResult` 格式一致。

#### I4：外部 MCP Server 管理 UI

**修改文件**：`src/WhiteBoard.UI/ViewModels/AiSettingsViewModel.cs`

新增：
```csharp
public ObservableCollection<McpClientConnectionItemViewModel> McpConnections { get; } = [];

public ICommand AddMcpConnectionCommand    { get; }
public ICommand RemoveMcpConnectionCommand { get; }
```

```csharp
public sealed class McpClientConnectionItemViewModel : ViewModelBase
{
    // 双向绑定属性：DisplayName, Transport, Command, Arguments,
    // Endpoint, ToolNamePrefix, TrustLevel, AllowDestructiveTools
    // 运行时只读：ConnectionStatus (Connected/Disconnected/Error), ToolCount
    
    public static McpClientConnectionItemViewModel From(McpClientConnectionSettings s) { ... }
    public McpClientConnectionSettings ToSettings() { ... }
}
```

---

## 关键文件变更矩阵

| 文件 | 操作 | 阶段 | 说明 |
|------|------|------|------|
| `WhiteBoard.Mcp.Core.csproj` | 新建 | H1 | 共享适配层项目 |
| `Registry/IMcpSessionRegistry.cs` | 新建 | H1 | Token 管理接口 |
| `Registry/McpSessionRegistry.cs` | 新建 | H1 | 内存 Token 存储 + 过期清理 |
| `Server/McpToolBridge.cs` | 新建 | H2 | AIFunction ↔ McpServerTool |
| `IPC/PipeContract.cs` | 新建 | H3 | 管道 NDJSON 消息契约 |
| `IPC/WhiteboardMcpPipeServer.cs` | 新建 | H3 | WPF 侧管道服务端 |
| `Hosting/EmbeddedMcpServerHost.cs` | 新建 | H4 | Kestrel 内嵌 HTTP/SSE 主机 |
| `Hosting/McpTokenAuthHandler.cs` | 新建 | H4 | Bearer Token 认证 |
| `Client/McpClientProvider.cs` | 新建 | I1 | 外部 MCP 服务导入 |
| `WhiteBoard.McpHost.csproj` | 新建 | H3 | stdio 控制台进程 |
| `WhiteBoard.McpHost/Program.cs` | 新建 | H3 | stdio 主机 + 管道代理 |
| `WhiteBoard.AI/Settings/AIWhiteboardSettings.cs` | 修改 | H5 | 新增 McpServer/McpClient 配置 |
| `WhiteBoard.AI/Session/WhiteboardAISession.cs` | 修改 | H6 | InitializeAsync, IssueMcpTokenAsync |
| `WhiteBoard.AI/Tools/WhiteboardToolRegistry.cs` | 修改 | I2 | RegisterExternalTools, GetFunctions |
| `WhiteBoard.AI/Session/AgentLoop.cs` | 修改 | I3 | 工具结果归一化 |
| `WhiteBoard.UI/ViewModels/AiSettingsViewModel.cs` | 修改 | H7/I4 | MCP 设置面板 ViewModel |
| `WhiteBoard.UI/ViewModels/AiChatViewModel.cs` | 修改 | H7 | MCP 活动指示器 |
| `WhiteBoard.sln` | 修改 | H1 | 注册两个新项目 |

---

## 风险与缓解

| 风险 | 严重度 | 缓解措施 |
|------|--------|---------|
| `ModelContextProtocol` SDK 仍是 preview，API 可能变更 | 高 | 隔离在 `WhiteBoard.Mcp.Core` 后，其他项目不直接依赖 |
| 破坏性工具被外部客户端绕过确认 | 高 | Phase H5：服务端 `AllowDestructiveTools` 门控，默认关闭 |
| stdio 需要 Claude Desktop 能找到 McpHost.exe | 中 | 打包时一并输出，文档说明 PATH 或绝对路径配置 |
| 外部 MCP 工具名称与内部工具冲突 | 中 | 强制前缀（`ext__` 或用户自定义）+ 冲突检测 |
| WPF STA 线程与 Kestrel 异步 I/O 冲突 | 中 | Kestrel 运行在自己的线程池，工具调用通过 Session 序列化器转发 |
| 多个外部客户端同时调用破坏性工具 | 低 | Session executor 队列保证原子性 |

---

## NuGet 包

```xml
<!-- WhiteBoard.Mcp.Core.csproj -->
<PackageReference Include="ModelContextProtocol"            Version="0.3.0-preview.1" />
<PackageReference Include="ModelContextProtocol.AspNetCore" Version="0.3.0-preview.1" />
<PackageReference Include="Microsoft.AspNetCore.App" />

<!-- WhiteBoard.McpHost.csproj -->
<PackageReference Include="ModelContextProtocol"            Version="0.3.0-preview.1" />
```

> **注意**：`ModelContextProtocol` 版本需与本地 .NET 10 缓存核对，使用 `dotnet package search ModelContextProtocol` 确认最新可用版本后固定。

---

## Claude Desktop 接入配置示例

```json
// claude_desktop_config.json
{
  "mcpServers": {
    "whiteboard": {
      "command": "C:\\Path\\To\\WhiteBoard.McpHost.exe",
      "env": {
        "WHITEBOARD_MCP_TOKEN": "<从白板设置页面复制>",
        "WHITEBOARD_MCP_PIPE_NAME": "whiteboard-mcp"
      }
    }
  }
}
```

---

## SESSION_ID（供 /ccg:execute 使用）

- CODEX_SESSION: 019eba63-fbe8-73e3-bec9-67d474ed5eec
- GEMINI_SESSION: N/A（环境未配置 GEMINI_API_KEY，已跳过）

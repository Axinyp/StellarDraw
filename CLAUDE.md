# StellarDraw — 智能白板独立仓库

> 本仓库从 ConferenceAIO 主仓（Gitee）的 `SignalR Demo/WhiteBoard/` 子目录迁出，
> 独立维护于 GitHub：https://github.com/Axinyp/StellarDraw

---

## 项目定位

可插拔白板组件库，面向 Windows 智能会议平板场景。支持：
- 多页白板 / 实时协同（SignalR）
- AI 助手对话（多 LLM 接入 + Tool Use）
- MCP Server/Client 双向集成（Claude Desktop / Cursor 可直接调用白板工具）
- 插件化架构（AssemblyLoadContext 热卸载）

---

## 目录结构

> 2026-08 重组：冻结代码物理隔离到 `legacy/`，`src/` 只保留活跃演进代码。
> 详细边界规则见 `docs/development-boundaries.md`（权威文档）。

```
StellarDraw/
├── src/                              活跃演进代码
│   ├── Axinyp.Wpf.Hosting.Abstractions/  框架稳定契约层
│   ├── Axinyp.Wpf.Hosting/               框架：Generic Host/模块/插件/设置/本地化
│   ├── Axinyp.Wpf.Hosting.Wpf/           框架：窗口/导航/对话框/主题/WPF 本地化
│   └── WhiteBoard.Client/                正式 Client Shell（业务回接进行中）
├── legacy/                           冻结的白板基线（只读参考，CI 持续验证）
│   ├── src/WhiteBoard.*/                 领域/渲染/AI/MCP/插件/Demo 宿主
│   ├── tests/WhiteBoard.*/               基线行为验证测试
│   └── StellarDraw.Legacy.slnx
├── consumers/StellarDraw.Axinyp.Spike/   包边界消费者验证
├── samples/BasicHost/                    框架样例
└── tests/                            活跃代码测试
    ├── Axinyp.Wpf.Hosting(.Wpf).Tests/
    ├── WhiteBoard.Client.Tests/
    ├── StellarDraw.Axinyp.Spike.Tests/
    └── TestSupport/
```

---

## 技术栈

- **运行时**：.NET 10, WPF (net10.0-windows)
- **渲染**：WPF DrawingVisual 分层（committed / wetInk / predicted / selection）
- **空间索引**：RBush R\*Tree（`WhiteBoard.Core/Domain/SpatialIndex.cs`）
- **对象变换**：`float[6]` Matrix3x2（行向量约定：x'=x·M11+y·M21+OffsetX）
- **撤销/重做**：Command 模式（`IWhiteboardCommand` → `UndoRedoStack`）
- **AI 接入**：OpenAI / Azure / 自定义 LLM，通过 `ILlmProvider` 抽象
- **MCP**：`ModelContextProtocol` NuGet，支持 stdio + SSE 双传输

---

## 关键文件速查

| 文件 | 职责 |
|------|------|
| `src/Axinyp.Wpf.Hosting/Extensions/HostApplicationBuilderExtensions.cs` | 框架入口：AddModule / AddStartupPlugins / BuildModularHost |
| `src/Axinyp.Wpf.Hosting.Wpf/Internal/NavigationService.cs` | 区域导航（门闩串行化 + 重入检测） |
| `src/Axinyp.Wpf.Hosting.Wpf/Internal/WindowManager.cs` | 窗口生命周期与 DI scope 管理 |
| `src/WhiteBoard.Client/App.xaml.cs` | Client 宿主入口（协调停机模式） |
| `legacy/src/WhiteBoard.Core/Domain/WhiteboardSession.cs` | Session 主入口，操作经执行器串行化 |
| `legacy/src/WhiteBoard.Core/Domain/SpatialIndex.cs` | R\*Tree 空间索引，HitTest / LassoSelect |
| `legacy/src/WhiteBoard.UI/Controls/InkSurface.cs` | WPF 画布，事件路由，分层 DrawingVisual |
| `legacy/src/WhiteBoard.UI/Rendering/InkRenderer.cs` | 所有对象类型的 DrawingContext 渲染 |
| `legacy/src/WhiteBoard.AI/Session/AgentLoop.cs` | LLM 多轮工具调用主循环 |
| `legacy/src/WhiteBoard.Mcp.Core/Hosting/EmbeddedMcpServerHost.cs` | MCP Server 嵌入宿主 |

---

## 构建 & 运行

```bash
# 框架（Axinyp 三层 + 样例 + 框架测试）
dotnet build WhiteBoard.slnx
dotnet test WhiteBoard.slnx

# 应用（Client Shell + 其测试；会联动构建 legacy 依赖链）
dotnet build StellarDraw.slnx
dotnet test StellarDraw.slnx

# 冻结基线验证
dotnet test legacy/StellarDraw.Legacy.slnx

# 运行 Client Shell
dotnet run --project src/WhiteBoard.Client

# 运行旧演示宿主（冻结参考）
dotnet run --project legacy/src/WhiteBoard.Host.Demo
```

---

## CCG 任务 & 计划

- 进行中任务：`.ccg/tasks/`
- 设计计划文档：`.claude/plan/`
  - `ai-whiteboard-design.md` — AI 层完整设计（Phases A-G + MCP 集成）
  - `mcp-integration.md` — MCP Server/Client 实施细节
  - `whiteboard-plugin-architecture.md` — 插件架构基础设计

---

## 编码约定

- 所有 Session 操作通过 `PostOperationAsync` 在执行器线程运行，禁止直接从 UI 线程修改领域对象
- 几何变更必须通过 `SpatialIndex.Update(obj, mutate)` 原子更新，保持索引同步
- Transform 矩阵采用行向量约定，旋转角顺时针为正
- 新增命令类实现 `IWhiteboardCommand`，通过 `_history.Execute(cmd)` 注册到撤销栈

---

## .context 项目上下文

> 项目使用 `.context/` 管理开发决策上下文。

- 编码规范：`.context/prefs/coding-style.md`
- 工作流规则：`.context/prefs/workflow.md`
- 决策历史：`.context/history/commits.md`

**规则**：修改代码前必读 prefs/，做决策时按 workflow.md 规则记录日志。

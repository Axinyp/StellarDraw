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

```
StellarDraw/
├── src/
│   ├── WhiteBoard.Core/          领域层：对象模型、命令、空间索引、Session
│   ├── WhiteBoard.UI/            WPF 渲染层：InkSurface、选择系统、ViewModel
│   ├── WhiteBoard.AI/            AI 层：AgentLoop、工具注册、语音管道
│   ├── WhiteBoard.Mcp.Core/      MCP Server + Client 核心
│   ├── WhiteBoard.McpHost/       MCP 独立控制台进程（stdio 传输）
│   ├── WhiteBoard.Plugin/        协同插件（SignalR 适配器、笔迹预测）
│   ├── WhiteBoard.Protocol/      序列化协议（操作 DTO）
│   ├── WhiteBoard.Contracts/     跨层接口契约
│   ├── WhiteBoard.Contracts.Wpf/ WPF 插件接口
│   └── WhiteBoard.Host.Demo/     演示宿主（可直接运行）
└── tests/
    ├── WhiteBoard.Core.Tests/    领域层单元测试（xUnit）
    ├── WhiteBoard.AI.Tests/      AI 层单元测试
    └── WhiteBoard.Protocol.Tests/ 协议序列化测试
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
| `src/WhiteBoard.Core/Domain/WhiteboardSession.cs` | Session 主入口，所有操作通过 `PostOperationAsync` 串行化 |
| `src/WhiteBoard.Core/Domain/SpatialIndex.cs` | R\*Tree 空间索引，HitTest / LassoSelect |
| `src/WhiteBoard.UI/Controls/InkSurface.cs` | WPF 画布，事件路由，分层 DrawingVisual |
| `src/WhiteBoard.UI/Controls/SelectionController.cs` | 选择系统：橡皮筋、移动、缩放、旋转手柄 |
| `src/WhiteBoard.UI/Rendering/InkRenderer.cs` | 所有对象类型的 DrawingContext 渲染 |
| `src/WhiteBoard.AI/Session/AgentLoop.cs` | LLM 多轮工具调用主循环 |
| `src/WhiteBoard.Mcp.Core/Hosting/EmbeddedMcpServerHost.cs` | MCP Server 嵌入宿主 |

---

## 构建 & 运行

```bash
# 构建全量
dotnet build WhiteBoard.slnx

# 运行演示宿主
dotnet run --project src/WhiteBoard.Host.Demo

# 运行测试
dotnet test
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

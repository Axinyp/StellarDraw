# StellarDraw — AI-Powered Whiteboard System

<p align="center">
  <img src="docs/banner.png" alt="StellarDraw" width="600"/>
</p>

<p align="center">
  <a href="#english">English</a> ·
  <a href="#中文">中文</a>
</p>

---

<a name="english"></a>

## English

StellarDraw is a WPF/.NET 10 intelligent whiteboard system with a plugin-based architecture and full AI integration. It supports natural-language-driven content creation via text or voice, exposes whiteboard tools to external AI clients (Claude Desktop, etc.) through the **Model Context Protocol (MCP)**, and can consume tools from external MCP servers.

### ✨ Features

| Category | Capability |
|----------|-----------|
| **AI Interaction** | Multi-turn conversation with automatic tool calling |
| **Drawing** | Natural-language shape/text/image insertion, flow chart generation |
| **Voice** | Push-to-Talk (PTT) and Wake-Word modes |
| **Multi-LLM** | Claude · GPT-4o · Gemini · Ollama (local) · Azure OpenAI · custom endpoints |
| **MCP Server** | Expose all whiteboard tools to Claude Desktop (stdio) or web clients (HTTP/SSE) |
| **MCP Client** | Import tools from any external MCP server into the AI agent |
| **Math** | LaTeX rendering, step-by-step equation solving, chart drawing |
| **Image Gen** | DALL-E 3 / Stable Diffusion integration via `generate_and_insert_image` |
| **Plugin Isolation** | `AssemblyLoadContext` hot-reload, no app restart required |

### 🏗 Architecture

```
src/
├── WhiteBoard.Contracts/       Shared plugin interfaces (IWhiteboardWpfPlugin)
├── WhiteBoard.Contracts.Wpf/   WPF-specific plugin contracts
├── WhiteBoard.Protocol/        Collaborative event definitions
├── WhiteBoard.Core/            Domain: WhiteboardSession, Commands, SelectionModel
├── WhiteBoard.Plugin/          Default plugin: ink surface + Kalman predictor
├── WhiteBoard.UI/              WPF views + ViewModels (AI chat sidebar, settings)
├── WhiteBoard.AI/              AI engine: AgentLoop, tools, voice, LLM providers
│   ├── Abstractions/           ILlmProvider, ISttProvider, IWakeWordDetector, IMcpTokenFactory
│   ├── Providers/              LlmProviderFactory (ollama/openai/anthropic/azure/custom)
│   ├── Services/               IWhiteboardAiActions — service layer isolating AI from Session
│   ├── Session/                WhiteboardAISession, AgentLoop, ProgressiveContextBuilder
│   ├── Tools/                  Drawing/Editing/Query/Navigation/Content/Math/ImageGen catalogs
│   ├── Voice/                  VoicePipeline, WhisperSttProvider, WindowsSpeechWakeWordDetector
│   └── Settings/               AIWhiteboardSettings (JSON-serializable)
├── WhiteBoard.Mcp.Core/        MCP infrastructure
│   ├── Registry/               McpSessionRegistry — token lifecycle, SHA-256 hashing
│   ├── Server/                 McpToolBridge, WhiteboardMcpServerTool
│   ├── IPC/                    Named-pipe contract (NDJSON) — WPF ↔ McpHost
│   ├── Hosting/                EmbeddedMcpServerHost (Kestrel), Bearer-token auth
│   └── Client/                 McpClientProvider — import tools from external MCP servers
├── WhiteBoard.McpHost/         Standalone stdio MCP proxy process for Claude Desktop
└── WhiteBoard.Host.Demo/       Demo host: plugin loader + AI sidebar wiring
```

### 🚀 Quick Start

#### Prerequisites

- Windows 10/11 x64
- .NET 10 SDK
- Visual Studio 2022 17.10+
- (Optional) Ollama for local LLM: `winget install Ollama.Ollama`

#### Build & Run

```bash
git clone https://github.com/Axinyp/StellarDraw.git
cd StellarDraw
dotnet build WhiteBoard.sln -c Release
```

Run the demo host:

```bash
# Minimal config — local Ollama
set WHITEBOARD_LLM_PROVIDER=ollama
set WHITEBOARD_LLM_ENDPOINT=http://localhost:11434
set WHITEBOARD_LLM_MODEL=qwen2.5
dotnet run --project src/WhiteBoard.Host.Demo -c Release
```

Use any LLM provider:

```bash
# OpenAI
set WHITEBOARD_LLM_PROVIDER=openai
set WHITEBOARD_LLM_MODEL=gpt-4o
set WHITEBOARD_LLM_APIKEY_REF=sk-...

# Anthropic Claude
set WHITEBOARD_LLM_PROVIDER=anthropic
set WHITEBOARD_LLM_MODEL=claude-opus-4-7
set WHITEBOARD_LLM_APIKEY_REF=sk-ant-...
```

Click **🤖 AI** in the top-right corner to open the AI sidebar.

### 🔌 Claude Desktop Integration

Generate a token in the AI Settings panel (MCP Server tab), then add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "whiteboard": {
      "command": "C:\\Path\\To\\WhiteBoard.McpHost.exe",
      "env": {
        "WHITEBOARD_MCP_TOKEN": "<token from settings>",
        "WHITEBOARD_MCP_PIPE_NAME": "whiteboard-mcp"
      }
    }
  }
}
```

### 🛠 Tool Catalog

| Category | Tools |
|----------|-------|
| Drawing | `draw_stroke` `insert_shape` `set_pen_color` `set_pen_thickness` |
| Editing | `undo` `redo` `select_objects` `move_selected` `delete_objects` `clear_page` |
| Content | `insert_text` `insert_image` |
| Query | `get_canvas_size` `list_objects` `get_object_details` `get_ocr_text` |
| Navigation | `add_page` `navigate_page` `zoom_in` `zoom_out` `reset_view` |
| Math | `render_latex` `solve_and_show` `draw_chart` |
| Image Gen | `generate_and_insert_image` |
| External | `{prefix}__{tool}` — any tool imported from an external MCP server |

### 🔑 Key Design Decisions

| ADR | Decision | Reason |
|-----|----------|--------|
| ADR-023 | `Microsoft.Extensions.AI` as LLM abstraction | Provider-agnostic, one-line switch |
| ADR-024 | 4-level progressive context | Avoid token waste |
| ADR-025 | `IWhiteboardAiActions` service layer | AgentLoop never directly touches Session |
| ADR-026 | `CompositeCommand` batch undo | Multi-step AI operations must undo atomically |
| ADR-029 | Whisper.net in subprocess | Native DLL blocks `ALC.Unload()` |
| ADR-035 | Custom `WhiteboardMcpServerTool` subclass | SDK 1.4.0 `AIFunctionMcpServerTool` is `internal` |
| ADR-041 | `IMcpTokenFactory` in Abstractions | Break AI ↔ Mcp.Core circular dependency |

### 📦 Dependencies

| Package | Purpose |
|---------|---------|
| `Microsoft.Extensions.AI` | LLM provider abstraction |
| `ModelContextProtocol` | MCP C# SDK |
| `Microsoft.AspNetCore` | Kestrel embedded HTTP server |
| `System.Speech` | Wake-word detection (Windows built-in) |

### 📄 License

MIT

---

<a name="中文"></a>

## 中文

StellarDraw 是一套基于 WPF / .NET 10 的 **AI 智能白板系统**，采用插件式架构，内置完整 AI 集成能力。用户可通过自然语言对话或语音驱动所有白板功能，并通过 **Model Context Protocol (MCP)** 将白板工具暴露给外部 AI 客户端（Claude Desktop 等），同时支持从外部 MCP 服务导入工具扩展能力。

### ✨ 核心功能

| 分类 | 能力 |
|------|------|
| **AI 交互** | 多轮对话 + 自动工具调用 |
| **绘图** | 自然语言生成形状/文字/图片，流程图一键生成 |
| **语音** | Push-to-Talk（按键说话）和 Wake-Word（唤醒词）两种模式 |
| **多 LLM** | Claude · GPT-4o · Gemini · Ollama（本地）· Azure OpenAI · 自定义端点 |
| **MCP 服务端** | 将所有白板工具暴露给 Claude Desktop（stdio）或 Web 客户端（HTTP/SSE） |
| **MCP 客户端** | 从任意外部 MCP 服务器导入工具到 AI 代理 |
| **数学** | LaTeX 渲染、方程逐步求解、图表绘制 |
| **AI 生图** | DALL-E 3 / Stable Diffusion 集成 |
| **插件隔离** | `AssemblyLoadContext` 热重载，无需重启应用 |

### 🏗 架构概览

```
src/
├── WhiteBoard.Contracts/       插件接口契约（IWhiteboardWpfPlugin）
├── WhiteBoard.Contracts.Wpf/   WPF 专用插件契约
├── WhiteBoard.Protocol/        协同事件定义
├── WhiteBoard.Core/            领域层：WhiteboardSession、命令、SelectionModel
├── WhiteBoard.Plugin/          默认插件：墨迹画布 + Kalman 预测器
├── WhiteBoard.UI/              WPF 视图 + ViewModel（AI 对话侧边栏、设置面板）
├── WhiteBoard.AI/              AI 引擎：代理循环、工具集、语音、LLM 提供商
│   ├── Abstractions/           ILlmProvider、ISttProvider、IWakeWordDetector、IMcpTokenFactory
│   ├── Providers/              LlmProviderFactory（5 种提供商）
│   ├── Services/               IWhiteboardAiActions — 服务层隔离 AI 与 Session
│   ├── Session/                WhiteboardAISession、AgentLoop、渐进式上下文构建器
│   ├── Tools/                  绘图/编辑/查询/导航/内容/数学/图像生成工具集
│   ├── Voice/                  语音管道、Whisper STT、Windows 唤醒词检测器
│   └── Settings/               AIWhiteboardSettings（JSON 可序列化）
├── WhiteBoard.Mcp.Core/        MCP 基础设施
│   ├── Registry/               McpSessionRegistry — Token 生命周期管理（SHA-256）
│   ├── Server/                 McpToolBridge、WhiteboardMcpServerTool
│   ├── IPC/                    命名管道契约（NDJSON）— WPF ↔ McpHost 进程通信
│   ├── Hosting/                EmbeddedMcpServerHost（Kestrel 内嵌）+ Bearer Token 认证
│   └── Client/                 McpClientProvider — 从外部 MCP 服务导入工具
├── WhiteBoard.McpHost/         独立 stdio MCP 代理进程（供 Claude Desktop 使用）
└── WhiteBoard.Host.Demo/       演示宿主：插件加载 + AI 侧边栏接线
```

### 🚀 快速开始

#### 环境要求

- Windows 10/11 x64
- .NET 10 SDK
- Visual Studio 2022 17.10+
- （可选）本地 LLM：`winget install Ollama.Ollama`

#### 构建与运行

```bash
git clone https://github.com/Axinyp/StellarDraw.git
cd StellarDraw
dotnet build WhiteBoard.sln -c Release
```

启动演示宿主：

```bash
# 最小配置 — 使用本地 Ollama
set WHITEBOARD_LLM_PROVIDER=ollama
set WHITEBOARD_LLM_ENDPOINT=http://localhost:11434
set WHITEBOARD_LLM_MODEL=qwen2.5
dotnet run --project src/WhiteBoard.Host.Demo -c Release
```

使用其他 LLM 提供商：

```bash
# OpenAI
set WHITEBOARD_LLM_PROVIDER=openai
set WHITEBOARD_LLM_MODEL=gpt-4o
set WHITEBOARD_LLM_APIKEY_REF=sk-...

# Anthropic Claude
set WHITEBOARD_LLM_PROVIDER=anthropic
set WHITEBOARD_LLM_MODEL=claude-opus-4-7
set WHITEBOARD_LLM_APIKEY_REF=sk-ant-...
```

点击主窗口右上角 **🤖 AI** 按钮展开 AI 侧边栏。

### 🔌 接入 Claude Desktop

在 AI 设置面板（MCP 服务端标签页）生成 Token，然后添加到 `claude_desktop_config.json`：

```json
{
  "mcpServers": {
    "whiteboard": {
      "command": "C:\\Path\\To\\WhiteBoard.McpHost.exe",
      "env": {
        "WHITEBOARD_MCP_TOKEN": "<从设置页面复制>",
        "WHITEBOARD_MCP_PIPE_NAME": "whiteboard-mcp"
      }
    }
  }
}
```

### 🛠 工具清单

| 分类 | 工具 |
|------|------|
| 绘图 | `draw_stroke` `insert_shape` `set_pen_color` `set_pen_thickness` |
| 编辑 | `undo` `redo` `select_objects` `move_selected` `delete_objects` `clear_page` |
| 内容 | `insert_text` `insert_image` |
| 查询 | `get_canvas_size` `list_objects` `get_object_details` `get_ocr_text` |
| 导航 | `add_page` `navigate_page` `zoom_in` `zoom_out` `reset_view` |
| 数学 | `render_latex` `solve_and_show` `draw_chart` |
| 图像生成 | `generate_and_insert_image` |
| 外部工具 | `{前缀}__{工具名}` — 从外部 MCP 服务器导入 |

### 🔑 关键技术决策

| ADR | 决策 | 原因 |
|-----|------|------|
| ADR-023 | `Microsoft.Extensions.AI` 作为 LLM 抽象层 | 提供商无关，一行切换 |
| ADR-024 | 4 级渐进式上下文 | 避免 token 浪费 |
| ADR-025 | `IWhiteboardAiActions` 服务层 | AgentLoop 不直接操作 Session |
| ADR-026 | `CompositeCommand` 批量撤销 | AI 多步操作必须整体撤销 |
| ADR-029 | Whisper.net 子进程隔离 | Native DLL 阻止 `ALC.Unload()` |
| ADR-035 | 自定义 `WhiteboardMcpServerTool` 子类 | SDK 1.4.0 `AIFunctionMcpServerTool` 为 internal |
| ADR-041 | `IMcpTokenFactory` 在 Abstractions 层 | 打破 AI ↔ Mcp.Core 循环依赖 |

### 📦 主要依赖

| 包 | 用途 |
|----|------|
| `Microsoft.Extensions.AI` | LLM 提供商抽象 |
| `ModelContextProtocol` | MCP C# SDK |
| `Microsoft.AspNetCore` | Kestrel 内嵌 HTTP 服务器 |
| `System.Speech` | 唤醒词检测（Windows 内置） |

### 📄 许可证

MIT

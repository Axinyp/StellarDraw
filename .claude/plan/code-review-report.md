# StellarDraw 全量业务模块代码审查报告

> 生成日期：2026-06-30
> 范围：Core / AI / UI / Mcp.Core / Plugin（约 1.2 万行）
> 方法：4 个并行审查 agent 分模块深读 + 主审人工核验关键项
> 状态：本报告为审查产出；C1–C4 修复进度见文末「修复跟踪」

---

## 一、Critical —— 功能性破坏，必须优先修

### C1. 空间索引删除/更新永久失效，索引与对象状态彻底不同步
`src/WhiteBoard.Core/Domain/SpatialIndex.cs:22-33,50-67`

`SpatialEntry` 是 `sealed class` 且未重写 `Equals`/`GetHashCode`，`RBush<SpatialEntry>` 用默认构造 → `Delete` 走 `EqualityComparer<T>.Default`（引用相等）。但 `MakeEntry(obj)` 每次 `new` 全新实例，`Insert` 存入的实例与 `Remove`/`Update` 传入的实例永远不同引用 → **删除恒为 no-op**。

已核验：`PageModel.HitTest/LassoSelect`（`PageModel.cs:72-76`）直接返回索引结果，无二次过滤。后果：删除对象后仍被框选/橡皮擦命中；每次平移/缩放/旋转把旧 AABB 留在树里并新增一条，索引条目无限膨胀。直接架空"几何变更通过 `SpatialIndex.Update` 保持同步"这一核心约定。

**修复**：让 `SpatialEntry` 实现 `IEquatable` 基于 `Object` 引用比较并重写 `GetHashCode`；或在 `SpatialIndex` 内用 `Dictionary<WhiteboardObject, SpatialEntry>` 缓存当前 entry，Remove/Update 时取同一实例传给 `Delete`。
**验证**：画两个对象 → 删除一个 → 在原位置框选，若仍被选中即复现。

### C2. AI 插入图像为占位实现，图像/数学渲染一律不上屏
`src/WhiteBoard.AI/Services/WhiteboardAiActionsImpl.cs:132-147`（已核验）

方法 `new` 了 `ImageObject image` 却从未入库，实际加入的是灰色占位 `Rectangle`；`spec.ImageData`（PNG 字节）被完全忽略；返回的 `created:[image.Id]` 是**未入库对象的 Id**，与画布真实矩形 Id 不一致 → LLM 后续按该 id/alias 引用、移动、删除全部失效；`alias` 也未 `RegisterAlias`。

影响链：`generate_and_insert_image`（DALL-E/SD）、`render_latex`、`solve_and_show` 全部依赖此方法 → 生成图与 LaTeX 渲染图永远不显示。

**修复**：真正调用图片入库（落地 `ImageData`/`Url`），注册 alias，返回真实入库对象 Id。

### C3. Undo/Redo 在执行器线程触发 UI 渲染 → 跨线程异常
`src/WhiteBoard.UI/ViewModels/WhiteboardViewModel.cs:159-175`（已核验）

`PostOperationAsync(...).AsTask().ContinueWith(_ => RequestRedraw(), ExecuteSynchronously)`。续体在执行器线程（`Channel` 单消费者线程）同步执行 → `RequestRedraw → RedrawRequested → InkSurface.RenderCommitted → _committedVisual.RenderOpen()` 在非 UI 线程操作 `DispatcherObject`，抛 `InvalidOperationException`。

对照：同文件 `ClearPage/InsertShape/InsertImage`（:234/252/289）都正确用了 `_dispatcher.BeginInvoke`，唯独 Undo/Redo 漏了。

**修复**：续体改为 `_dispatcher.BeginInvoke(RequestRedraw, DispatcherPriority.Render)`。

### C4. 语音转写文本跨线程写入绑定集合 → 崩溃
`src/WhiteBoard.UI/ViewModels/AiChatViewModel.cs:285`（`OnVoiceTextReady`）

`VoiceTextReady` 由 STT 回调线程触发（`WhiteboardAISession.cs:167`），`OnVoiceTextReady` 直接 `Messages.Add(...)`，而 `Messages` 已绑定 `ItemsControl`。非 UI 线程修改绑定集合抛异常。设计文档 `ai-whiteboard-design.md:538` 明确要求 `Dispatcher.Invoke`，实现漏掉。

**修复**：用 `Application.Current?.Dispatcher.BeginInvoke` 包裹。

---

## 二、High —— 正确性/一致性缺陷

| 编号 | 位置 | 问题 |
|------|------|------|
| H1 | `Core/Commands/UndoRedoStack.cs:52-80` | `UndoObject` 执行逆命令后既不压回 undo 栈也不入 redo 栈，直接丢弃命中命令 → 按对象撤销不可重做、栈状态与页面发散 |
| H2 | `AI/Voice/WhisperSttProvider.cs:58-61` | catch 返回 `"[Whisper error:...]"` 作为有效识别文本，被当成用户语音发给 LLM，异常被吞成正常对话 |
| H3 | `AI/Providers/ImageGen/StableDiffusionProvider.cs:28-29` | `IsAvailable` getter 内 `GetAsync().GetAwaiter().GetResult()` sync-over-async，UI 线程下死锁风险；注释端点与实际请求不符 |
| H4 | `Plugin/Collaboration/SignalRCollaborationAdapter.cs:41-56` | 配了 `WithAutomaticReconnect` 但从不订阅 `Reconnecting/Reconnected/Closed`：重连后不重新同步，重连耗尽后无人重启且无日志 |
| H5 | 同上 `:66-75` | 重连窗口期 `State != Connected` 直接 `return` 丢弃本地事件，无 outbox 重发缓冲 |
| H6 | `Mcp.Core/IPC/WhiteboardMcpPipeClient.cs:42-49` | `SendAndReceiveAsync` 无 `SemaphoreSlim` 串行化，并发工具调用 NDJSON 帧错配、响应错配 |
| H7 | `UI/Controls/InkSurface.cs:362,518` | `OnTouchUp`/`OnMouseLeftButtonUp` 为 `async void`，内部 await 链含 `Matrix.Invert()`，异常无法捕获 → 崩溃 |
| H8 | `UI/Controls/SelectionController.cs:495-513` | `CommitResizeAsync` 仅对整体包围盒做下限，单对象可被压到亚像素；`sx/sy` 无上限致坐标溢出 |

---

## 三、Medium —— 健壮性/边界

- **Core** `WhiteboardSession.cs:106-137` 等：`done.Task.WaitAsync(ct)` 取消后 op 仍在执行器排队执行，"取消"是假象。
- **Core** `WhiteboardSession.cs:143-171`/`:455-464`：先全部执行删除再逐个 `await` 广播，某次广播抛异常被吞 → 本地已删、远端只收部分事件，协同发散。
- **Core** `PageSerializer.cs:191`：`Convert.FromHexString(AssetId)` 对非 hex AssetId 抛 `FormatException`，可破坏整页序列化。
- **AI** `Services/ToolResult.cs:39-54`：`ToJson()` 手写拼接 JSON，含特殊字符时生成非法 JSON（应改 `JsonSerializer.Serialize`）。
- **AI** `Session/AgentLoop.cs:189-207`：`AssembleAssistantMessage` 按 `Arguments.Count` 去重重组流式工具调用，与 OpenAI 增量 arguments 协议不符。
- **Mcp.Core** Pipe/Server 与 `WhiteboardMcpServerTool.cs:36-43`：跨边界参数 value 是 `JsonElement` 未归一化为 CLR 类型，工具取值失败。
- **Mcp.Core** `Registry/McpSessionRegistry.cs:49`：binding 持 `WeakReference`，GC 后工具调用静默失败。
- **Mcp.Core** `Hosting/EmbeddedMcpServerHost.cs:80-84`：`DisposeAsync` 不先 `StopAsync`，强行中断在途 SSE。
- **Mcp.Core** `McpClientProvider.cs:39-42,66-69`：外部 MCP 失败仅 `Debug.WriteLine`，Release 下完全静默。
- **Plugin** `SignalRCollaborationAdapter.cs:104-114`：去重 `HashSet` 满则整体 `Clear()`，乱序重传的旧 EventId 被二次应用。
- **UI** `InkSurface.cs:427,546`+`:606-611`：视图缩放无上下限，接近奇异时 `Matrix.Invert()` 抛异常无保护。
- **UI** `WhiteboardViewModel.cs:48-49`：Undo/Redo 命令 `CanExecute` 不随历史栈刷新。
- **AI** `Settings/WindowsCredentialStore.cs:27,78`：持久化 `CRED_PERSIST_LOCAL_MACHINE` 与注释"当前用户隔离"矛盾。

低优先（Low）：`ResizeBoundsCommand.cs:83` 旋转对象缩放计算错误、`PathObject.cs:53` SVG 弧线参数当坐标致 AABB 错误、`WhiteboardSession.cs:751-795` 死代码；`WhiteboardMcpPipeServer.cs:56-60` accept 循环无退避忙循环；UI 控件 `Unloaded` 未注销订阅、空 `GuidelineSet`、浮动工具栏无边界钳制。

---

## 四、遗漏 / 未完成功能

- **图像插入整体未落地**（见 C2），连带 DALL-E/SD/LaTeX 全链路。
- **多页能力未接通**：`PageManager` 有增删页方法，但 `WhiteboardSession` 未暴露对应 `PostOperationAsync` 入口；`ApplyRemoteEvent` 的 `PageAddedEvent` 仅 `EnsureAtLeastOnePage()`，远端加页本地不可见（`WhiteboardSession.cs:902-904`）。
- **协同事件覆盖不全**：`HubEnvelopeSerializer.Wrap/Unwrap` 仅覆盖 4 类事件，其余子类 `Wrap` 返回 null 被静默丢弃。
- **Anthropic Provider 名不副实**：`LlmProviderFactory.CreateAnthropic` 实际走 OpenAI 兼容客户端（`:58-65`）。
- **数学渲染非真实排版**：`WpfMathRenderer` 仅 LaTeX→Unicode 字符串替换。
- **协同插件消息处理是占位**：`WhiteboardPlugin.ProcessMessagesAsync` 仅 `Debug.WriteLine`（`:143-151`）。
- **MCP 连接通知不完整**：`ConnectionOpened` 声明但从不触发；`Cleanup` 移除时不触发 `ConnectionClosed`。
- **无 LLM 重试/单次超时**：`AgentLoop.IsRetryable` 仅打标记；`maxTurns=20` 跑满后静默结束。
- **STT 工厂未用 Whisper 本地模型字段**；**序列化 schema 迁移未实现**。

未发现真正的 `TODO/FIXME/NotImplementedException`，亦未发现密钥硬编码或写入日志。

---

## 五、注释缺失清单（按模块）

- **Core**：`WhiteboardSession` 旋转 AABB 矩阵推导、`AddImageCommand`（整类缺 summary）、`ResizeBoundsCommand.ApplyResize`、`LassoHitTest`、各 Object 双构造函数语义。
- **AI**：四个 `Insert*Async`、`ParseColor`/`Resolve`/`ResolveAll`、`MathToolCatalog` 图表常量、`AgentLoop.AssembleAssistantMessage`、Mcp 配置类。
- **UI**：`InkSurface.OnRendering`/`BlendPredictions`、`SelectionController.ComputeNewScreenBounds`/`CommitResizeAsync`、`InkRenderer.BuildStrokeGeometry`、`PredictedInkRenderer`、`RelayCommand`。
- **Mcp.Core**：`McpSessionRegistry`、`McpTokenAuthHandler`、`McpClientProvider`、`PipeContract`、`WhiteboardMcpPipeClient`。
- **Plugin**：`LinearPredictor`/`KalmanPredictor.Predict` 的魔法常量。

---

## 六、修复优先级

1. **立即**：C1、C3、C4。
2. **本迭代**：C2 + 图像功能落地、H1、H2、H4/H5。
3. **排期**：H3/H6/H7/H8 与 Medium 项；遗漏功能按产品优先级。
4. 注释补充搭车对应 bug 修复一并完成。

---

## 修复跟踪

| 编号 | 状态 | 备注 |
|------|------|------|
| C1 SpatialIndex 删除失效 | 已修复 | 用 `Dictionary<WhiteboardObject,SpatialEntry>` 缓存 entry 实例，Remove/Update 取同一引用删除；编译+测试通过 |
| C2 InsertImage 占位 | 已修复 | 新增 Core 跨层 `RawImageAssetStore`（byte[]）；AI 落地字节+注册 alias+返回真实 Id；UI 渲染未命中时回退解码并回填 WPF 缓存 |
| C3 Undo/Redo 跨线程 | 已修复 | 续体改 `_dispatcher.BeginInvoke(RequestRedraw, DispatcherPriority.Render)`，与同文件其他操作一致 |
| C4 语音文本跨线程 | 已修复 | `OnVoiceTextReady` 经 `Application.Current.Dispatcher` marshal 回 UI 线程 |

### 修复验证
- `dotnet build WhiteBoard.slnx`：0 错误，6 警告（既有）。
- `dotnet test`：**全部通过**（Core 16 / Protocol 24 / AI 51，共 91 个，0 失败）。

### High 级修复跟踪

| 编号 | 状态 | 备注 |
|------|------|------|
| H1 UndoObject 破坏 redo 栈 | 已修复 | 命中命令撤销后 `_redoStack.Push(cmd)` 使其可重做；注：UndoObject 当前无调用方（未接线功能），仅修正逻辑正确性 |
| H2 Whisper 错误文本污染对话 | 已修复 | catch 改为抛 `InvalidOperationException`，由 `VoicePipeline` 经 `ErrorOccurred` 通道处理，不再返回伪文本 |
| H3 StableDiffusion sync-over-async | 部分 | 仅修正注释端点不符；根治需将 `IImageGenProvider.IsAvailable` 异步化（接口改动），见下「待办」 |
| H6 Pipe 客户端并发帧错配 | 已修复 | `SemaphoreSlim(1,1)` 串行化 send→receive 往返，Dispose 释放 |
| H7 async void 事件处理器异常 | 已修复 | `OnTouchUp`/`OnMouseLeftButtonUp` 包 try/catch 兜底，防应用级崩溃 |
| H8 CommitResize 单对象退化 | 已修复 | 单对象最终 W/H 加 0.5px 防御性下限，防按比例缩放退化为 0/负 |
| 预存：PathObject 包围盒 padding | 已修复 | `ComputeBounds` 去除 `strokeWidth*0.5` 外扩，改为纯几何坐标（与 ShapeObject 一致），3 个测试转绿 |

### 待办（需设计决策 / 接口改动，未在本轮处理）

- **H3 根治**：`IsAvailable` 是同步属性却内含网络探测（首次阻塞 2 秒），正确修法是改为 `Task<bool> IsAvailableAsync()` 并异步化 `WhiteboardToolRegistry` 构造。后台预热会破坏 registry 同步注册时序，故未采用。
- **H4/H5 协同断线一致性**：SignalR 重连后需"重新同步"（请求增量/全量快照）+ 断线期本地事件 outbox 重发。两者都依赖 Hub 协议层提供同步端点，最小补丁会造成"已修复"错觉，需作为独立设计任务推进。
- Medium/Low 项（`ToolResult.ToJson` 转义、`JsonElement` 跨边界归一化、协同事件全覆盖、视图缩放钳制等）与遗漏功能（多页、Anthropic 原生协议、真实 LaTeX 渲染）按产品优先级排期。

---

## 第三轮：用户报告的三个功能问题 + Medium 项

> 三个问题先经并行 agent 根因诊断，再实施。全部编译 0 错误、91 测试全绿。

### #1 幽灵拖拽 → 实时跟随（方案 B，移动+缩放+旋转全改）
根因：拖动时覆盖层画临时副本、committed 层原件不动 → 重影。
- `InkRenderer.RenderPage` 加 `excludeIds` 参数，渲染时跳过指定对象。
- `InkSurface` 新增 `_hiddenObjectIds` + `SetHiddenObjects()`，RenderCommitted 时排除；创建 SelectionController 时传入隐藏回调。
- `SelectionController`：OnDown 进入移动/缩放/旋转时隐藏 committed 原件；OnUpAsync 提交后恢复；`RenderResizePreview`/`RenderRotatePreview` 改为绘制对象本体（缩放用 anchor→preview 矩阵，旋转用 RotateTransform+view），不再只画选框。

### #2 MCP 服务/Token（宿主层 wiring 全缺）
根因：`EmbeddedMcpServerHost` 从未实例化（Host.Demo 未引用 Mcp.Core）；token 工厂未注入；`McpTokenIssued`/`RegenerateMcpTokenRequested` 无订阅者。
- `Host.Demo.csproj` 加 `WhiteBoard.Mcp.Core` 项目引用。
- `MainWindow`：`InitAiSession` 异步化；新增 `InitMcpAsync`——创建 `McpSessionRegistry` + `McpSessionRegistryTokenFactory`、`SetMcpTokenFactory`、订阅 `McpTokenIssued` 回写 `CurrentMcpToken`（Dispatcher）、接通「重新生成」命令、按需启动 Kestrel、签发首个 token；`DisposeAiSessionAsync` 按 host→session→registry 顺序释放。

### #3 图片生成绘图（Provider 从未创建注入）
根因：`WhiteboardAISession` 构造未传 imageGen（恒 null）→ 工具从不注册；缺工厂；设置 UI 无入口；DALL-E `IsAvailable` 恒 true。
- 新增 `ImageGenProviderFactory`（仿 VoiceProviderFactory）。
- `MainWindow.InitAiSessionAsync` 创建并注入 imageGen。
- `DallE3Provider.IsAvailable` 改为校验 API Key 非空。
- `AiSettingsViewModel` ImageGen 字段升级为 public 属性 + provider 列表 + 可见性；`AiSettingsView.xaml` 新增「图像生成」配置分组。

### Medium 小修
- `ToolResult.ToJson` 改用 `JsonSerializer` 序列化，杜绝特殊字符产生非法 JSON。
- `InkSurface` 视图缩放加 `ApplyZoomClamped`，累计缩放钳制在 [0.1, 10]，防奇异矩阵/数值爆炸。

### 本轮未做（标注留待）
- **JsonElement 跨边界归一化**：`Microsoft.Extensions.AI` 的 `AIFunction` 很可能原生支持 `JsonElement` 绑定，未经实测不贸然手动转换，留待实测确认。
- **H3 根治**（IsAvailable 异步化）、**H4/H5 协同断线一致性**：仍为接口/设计任务。

---

## 修复跟踪（Critical）


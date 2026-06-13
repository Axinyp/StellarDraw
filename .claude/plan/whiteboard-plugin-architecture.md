# WhiteBoard Plugin Architecture Plan
> 整合自：架构头脑风暴 + Codex 审查 Round 1 + 19 个设计细节 + Codex 审查 Round 2（2026-06-09）
> SESSION_ID（Codex）: 019eaa01-7204-7573-bc60-5dd0e1439aec

---

## 一、项目目标

### 1.1 背景

将 ConferenceAIO 主程序中深度耦合的白板模块（~84 文件，~18k 行）完整抽离，
重新设计为基于 `AssemblyLoadContext` 的**可插拔、可热卸载**独立插件，
并解决现有架构中"普通白板 / 协同白板"两套代码的根本问题。

**目标路径**：`E:\Demo\demo\SignalR Demo\WhiteBoard\`
**InkForecast**：`E:\Demo\demo\SignalR Demo\SynergyDemo\InkForecast\`（笔迹前向预测，独立引用）
**目标运行时**：`net10.0-windows`（直接跳过 .NET 6，充分利用 Dynamic PGO + DATAS GC）

### 1.2 功能目标

| 类别 | 目标 |
|------|------|
| 基础书写 | 支持笔、荧光笔、马克笔、橡皮擦；压感粗细；贝塞尔平滑 |
| 富内容对象 | 图片插入、文字输入、思维导图、表格、嵌入文档（文档协同批注） |
| 多页管理 | 多页增删/跳转/缩略图预览；页面背景可配置 |
| 撤销/重做 | 全局线性 + 按对象独立撤销，最深 200 步 |
| 保存/导出 | `.wbd` 原子保存；支持图片/PDF 导出；SHA256 资产去重 |
| 协同 | 协同为透明中间件，白板核心无感知；实时多人笔迹同步 |
| OCR | 手写文字识别，策略可切换（本地 WindowsInk / PaddleOCR / 云端 Azure） |
| 笔迹预测 | 前向预测填补帧间空白，肉眼感知延迟 < 20ms（v1） |

### 1.3 架构目标

| 指标 | 目标值 |
|------|--------|
| 插件热卸载 | 支持；ALC 泄漏验证：1000 次循环均通过 WeakReference 检查 |
| 主程序依赖 | 零 Prism / 零 DryIoc / 零 SignalR 直接引用 |
| 协同解耦 | 单机 / 协同通过 `ICollaborationAdapter` 切换，白板核心零感知 |
| 笔迹延迟（v1） | ≤ 20ms 肉眼感知（L1：双层 DrawingVisual + 前向预测） |
| 笔迹延迟（v2+） | 路线图规划到 ≤ 10ms / ≤ 7ms（见第九章延迟优化路线图） |
| 文件格式 | `.wbd` ZIP 容器，局部块损坏可恢复，支持跨版本迁移 |
| 测试覆盖 | Core 层 UndoRedo / 序列化 / 协同回放 100% 覆盖；ALC 浸泡测试 |

### 1.4 非目标（当前版本）

- 不实现 iPad/Android 端（仅 Windows WPF）
- 不实现 OT/CRDT 协同冲突算法（v1 为 Last-Write-Wins，v2 升级）
- 不实现晚加入/断线重连（v1.1 规划）
- 不实现权限模型细粒度控制（v1.1 规划）

---

## 二、现有问题根因

| 问题 | 根因 | 文件证据 |
|------|------|----------|
| 普通白板 / 协同白板两套代码 | 协同逻辑渗透进画布核心 | `WhiteBoardContentGrid.cs:59`、`SynergyHelper.cs:27` |
| 单文件过万行 | UI、业务、协同、保存混合 | `ContentOperation.xaml.cs` 55,664 行 |
| 框架强绑定 | 直接依赖 Prism EventAggregator × 17 个事件 | `WhiteboardMenu.xaml.cs:73` |
| 保存逻辑嵌入 UI | SaveHelper 在菜单层调用 | `WhiteboardMenu.xaml.cs:863` |
| OCR 混入画布 | `InkExtensionHelper` 在 `InkCanvasRe` 中直接调用 | `InkCanvasRe.cs` |

---

## 三、解决方案总览

### 核心设计原则

1. **一套白板**：协同作为透明中间件，白板核心永远不知道协同存在
2. **AssemblyLoadContext 隔离**：插件与宿主类型隔离，支持热卸载
3. **消息总线通信**：宿主 ↔ 插件解耦，不共享框架（不传 Prism/DryIoc）
4. **能力注入替代 IServiceProvider**：插件只能拿到宿主显式开放的服务
5. **Contracts 两层分离**：平台无关契约（netstandard2.0）与 WPF 契约（net10.0-windows）分开
6. **Document Space**：所有持久化坐标使用文档空间（96dpi 无关），屏幕坐标仅在渲染层存在
7. **单一变更通道**：所有 Session 状态变更序列化通过同一 Channel executor，消除并发写竞争

---

## 四、项目结构

```
E:\Demo\demo\SignalR Demo\WhiteBoard\
│
├── WhiteBoard.sln
│
├── src/
│   │
│   ├── WhiteBoard.Contracts/              (netstandard2.0) ← 平台无关契约
│   │   ├── Plugin/
│   │   │   ├── IPlugin.cs                 ← 生命周期基接口（无 WPF 依赖）
│   │   │   ├── IPluginMessageBus.cs
│   │   │   ├── IPluginSubscription.cs     ← 订阅句柄（含 IAsyncDisposable）
│   │   │   └── IHostCapabilities.cs
│   │   ├── Services/
│   │   │   ├── ICollaborationAdapter.cs
│   │   │   ├── ISaveService.cs
│   │   │   ├── IOcrStrategy.cs
│   │   │   ├── OcrResult.cs
│   │   │   └── IStrokePredictor.cs
│   │   └── Models/
│   │       ├── PluginMessage.cs           ← Payload 改为 ReadOnlyMemory<byte>
│   │       ├── WhiteboardEvent.cs
│   │       ├── SessionCapability.cs
│   │       └── Topics.cs
│   │
│   ├── WhiteBoard.Contracts.Wpf/          (net10.0-windows) ← WPF 专属契约
│   │   ├── IWhiteboardWpfPlugin.cs        ← 继承 IPlugin，新增 CreateView()
│   │   └── IPluginViewHandle.cs           ← FrameworkElement View（WPF 类型）
│   │
│   ├── WhiteBoard.Protocol/               (netstandard2.0)
│   │   ├── Operations/
│   │   │   ├── WhiteboardOperation.cs
│   │   │   └── OpType.cs
│   │   ├── Dtos/
│   │   │   ├── ObjectDto.cs               ← 通用对象 DTO（含 ObjectKind 枚举）
│   │   │   ├── StrokeDto.cs
│   │   │   ├── PageDto.cs
│   │   │   └── WhiteboardDocument.cs      ← manifest.json 反序列化模型
│   │   └── Serialization/
│   │       └── ProtocolVersion.cs
│   │
│   ├── WhiteBoard.Core/                   (net10.0，零 WPF 依赖)
│   │   ├── Domain/
│   │   │   ├── WhiteboardSession.cs       ← 聚合根 + Session Executor Channel
│   │   │   ├── PageModel.cs
│   │   │   ├── Objects/
│   │   │   │   ├── WhiteboardObject.cs
│   │   │   │   ├── StrokeObject.cs
│   │   │   │   ├── ImageObject.cs
│   │   │   │   ├── MindMapObject.cs
│   │   │   │   ├── TableObject.cs
│   │   │   │   ├── DocumentObject.cs
│   │   │   │   └── TextObject.cs
│   │   │   └── SpatialIndex.cs
│   │   ├── Commands/
│   │   │   ├── IWhiteboardCommand.cs      ← 含 SupportsObjectUndo/CreateObjectUndo
│   │   │   ├── AddObjectCommand.cs
│   │   │   ├── DeleteObjectCommand.cs
│   │   │   ├── TransformCommand.cs        ← 单/批对象变换
│   │   │   └── UndoRedoStack.cs
│   │   ├── Services/
│   │   │   ├── PageManager.cs
│   │   │   ├── PageSerializer.cs          ← page.bin 读写（strokes.bin 已更名）
│   │   │   └── OcrService.cs
│   │   └── Algorithms/
│   │       ├── BezierSmoothing.cs
│   │       ├── LassoHitTest.cs
│   │       └── PressureCalculator.cs
│   │
│   ├── WhiteBoard.UI/                     (net10.0-windows)
│   │   ├── Controls/
│   │   │   ├── InkSurface.xaml
│   │   │   ├── InkToolbar.xaml
│   │   │   ├── PageStrip.xaml
│   │   │   └── SelectionOverlay.xaml
│   │   ├── ViewModels/
│   │   │   ├── WhiteboardViewModel.cs
│   │   │   ├── ToolbarViewModel.cs
│   │   │   └── PageListViewModel.cs
│   │   └── Rendering/
│   │       ├── InkRenderer.cs
│   │       └── PredictedInkRenderer.cs
│   │
│   ├── WhiteBoard.Plugin/                 (net10.0-windows)
│   │   ├── WhiteboardPlugin.cs            ← 实现 IWhiteboardWpfPlugin
│   │   ├── WhiteboardPluginContext.cs
│   │   ├── WhiteboardPluginViewHandle.cs
│   │   └── InkForecast/
│   │       ├── KalmanPredictor.cs
│   │       └── LinearPredictor.cs
│   │
│   └── WhiteBoard.Host.Demo/              (net10.0-windows WPF)
│       ├── PluginManager.cs
│       ├── MockCollaborationAdapter.cs
│       ├── LocalSaveService.cs
│       └── MainWindow.xaml
│
└── tests/
    ├── WhiteBoard.Core.Tests/
    ├── WhiteBoard.Protocol.Tests/
    └── WhiteBoard.UI.Tests/
```

---

## 五、关键接口设计

### 5.1 插件生命周期（严格状态机）

```
[Unloaded] → InitializeAsync() → [Initialized]
                                       ↓ CreateView() (UI线程)
                                  [ViewCreated]
                                       ↓ StartAsync()
                                   [Running]
                                       ↓ StopAsync() (幂等)
                                   [Stopped]
                                       ↓ DetachAsync() (UI线程)
                                   [Detached]
                                       ↓ DisposeAsync()
                                   [Disposed]
```

```csharp
// WhiteBoard.Contracts (netstandard2.0) — 无 WPF 依赖
public interface IPlugin : IAsyncDisposable
{
    string PluginId { get; }
    string Version  { get; }
    // ct 在 StopAsync 时取消，让 InitializeAsync 中的后台任务感知停止
    Task InitializeAsync(IPluginMessageBus bus, IHostCapabilities host, CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);   // 幂等，可重复调用
}

// WhiteBoard.Contracts.Wpf (net10.0-windows) — WPF 专属，继承 IPlugin
public interface IWhiteboardWpfPlugin : IPlugin
{
    // 必须在 InitializeAsync 之后、StartAsync 之前调用；UI 线程执行
    IPluginViewHandle CreateView();
}

public interface IPluginViewHandle : IAsyncDisposable
{
    string           PluginId { get; }
    FrameworkElement View     { get; }
    // 必须在 DisposeAsync 之前调用；UI 线程执行；清除 DataContext/CommandBinding/ResourceReference
    Task DetachAsync(CancellationToken ct);
}
```

### 5.2 宿主能力接口

```csharp
public interface IHostCapabilities
{
    IHostDialogService  Dialog         { get; }
    IHostFilePicker     Files          { get; }
    IHostTelemetry      Metrics        { get; }
    IHostAuthContext    Auth           { get; }
    IHostLogger         Logger         { get; }
    IHostSettings       Settings       { get; }
    IHostShortcuts      Shortcuts      { get; }
    CultureInfo         CurrentCulture { get; }
}
```

### 5.3 消息总线（含订阅句柄）

```csharp
// Payload 使用 ReadOnlyMemory<byte>，避免 JsonElement 的 JsonDocument 生命周期问题
public sealed record PluginMessage(
    string                MessageId,
    string                Topic,
    string                SourcePluginId,
    string?               TargetPluginId,
    long                  Sequence,
    string                SchemaVersion,
    string?               CorrelationId,
    DateTimeOffset        OccurredAtUtc,
    ReadOnlyMemory<byte>  PayloadUtf8,            // UTF-8 JSON，生命周期自管理
    string                ContentType = "application/json"
);

// 订阅句柄：Dispose 自动注销并释放 Channel
public interface IPluginSubscription : IAsyncDisposable
{
    IAsyncEnumerable<PluginMessage> Messages { get; }
}

public interface IPluginMessageBus
{
    ValueTask PublishAsync(PluginMessage message, CancellationToken ct = default);

    // 返回订阅句柄，Dispose 释放该订阅
    IPluginSubscription Subscribe(
        string pluginId,
        Func<PluginMessage, bool>? filter = null);
}

// 实现要求：
// 1. 每个订阅者独立有界 Channel
// 2. per-topic backpressure 策略：命令类 Wait；预测/遥测类 DropOldest
// 3. Channel 满时丢弃并上报 IHostTelemetry，不静默挂起
// 4. 单订阅者内有序，不保证跨订阅者全局有序
// 5. PublishAsync 不阻塞 WPF UI 线程
```

### 5.4 协同透明中间件

```csharp
public interface ICollaborationAdapter
{
    bool IsActive { get; }
    ValueTask OnLocalEventAsync(WhiteboardEvent evt);
    IAsyncEnumerable<WhiteboardEvent> RemoteEventsAsync(CancellationToken ct);
}

public record WhiteboardOperation(
    string   OperationId,
    string   ClientId,
    string   PageId,
    string   ObjectId,
    long     BaseVersion,
    long?    ServerSeqNo,   // 服务端权威序号，null = 待确认
    OpType   Type
);

// v1 冲突策略：Last-Write-Wins（ServerSeqNo 大的覆盖小的），ADR-013

public sealed class NullCollaborationAdapter : ICollaborationAdapter
{
    public static readonly NullCollaborationAdapter Instance = new();
    public bool IsActive => false;
    public ValueTask OnLocalEventAsync(WhiteboardEvent evt) => ValueTask.CompletedTask;
    public async IAsyncEnumerable<WhiteboardEvent> RemoteEventsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        yield break;
    }
}
```

### 5.5 Session 变更序列化执行器（消除并发写）

```csharp
// 核心原则：所有 WhiteboardSession 状态变更（本地输入 + 远端事件）
// 都通过同一 Channel 序列化，不允许在 UI/后台线程直接写 Session 状态
public class WhiteboardSession
{
    private readonly ICollaborationAdapter _collab;
    private readonly UndoRedoStack         _history;
    private readonly PageManager           _pages;
    private readonly OcrService            _ocr;      // 非 nullable，构造时创建
    private          IStrokePredictor?     _predictor;

    // 单一变更通道：本地输入和远端事件都 Post 到此，确保序列化执行
    private readonly Channel<Func<ValueTask>> _ops =
        Channel.CreateUnbounded<Func<ValueTask>>();

    public WhiteboardSession(ICollaborationAdapter? collab = null)
    {
        _collab  = collab ?? NullCollaborationAdapter.Instance;
        _history = new UndoRedoStack(maxDepth: 200);
        _pages   = new PageManager();
        _ocr     = new OcrService();
    }

    // 外部调用：Post 到 Channel，不直接执行
    public ValueTask PostOperationAsync(Func<ValueTask> op, CancellationToken ct = default)
        => _ops.Writer.WriteAsync(op, ct);

    // 由 StartAsync 启动，持续消费队列
    private async Task RunExecutorAsync(CancellationToken ct)
    {
        await foreach (var op in _ops.Reader.ReadAllAsync(ct))
            await op();
    }

    // 远端事件通过 Post 进入同一队列
    public async Task StartRemoteListenAsync(CancellationToken ct)
    {
        await foreach (var evt in _collab.RemoteEventsAsync(ct))
        {
            if (ct.IsCancellationRequested) break;  // 防止 StopAsync 后仍处理晚到事件
            await PostOperationAsync(() => { ApplyRemoteEvent(evt); return ValueTask.CompletedTask; }, ct);
        }
    }

    public void RegisterPredictor(IStrokePredictor p) => _predictor = p;
}
```

### 5.6 AssemblyLoadContext 设计

```csharp
public class WhiteboardPluginContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public WhiteboardPluginContext(string pluginDllPath)
        : base("WhiteboardPlugin", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginDllPath);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        if (_sharedAssemblies.Contains(name.Name))
            return null;  // 回退 Default Context

        var path = _resolver.ResolveAssemblyToPath(name);
        return path != null ? LoadFromAssemblyPath(path) : null;
    }

    // 精确 HashSet 匹配，不用前缀通配
    // WhiteBoard.Contracts.Wpf 也需共享（其类型跨 ALC 边界传递）
    private static readonly HashSet<string> _sharedAssemblies =
        new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsBase",
        "PresentationCore",
        "PresentationFramework",
        "System.Xaml",
        "WhiteBoard.Contracts",
        "WhiteBoard.Contracts.Wpf",   // IWhiteboardWpfPlugin / IPluginViewHandle 需跨边界
        "WhiteBoard.Protocol",
    };
}
```

### 5.7 热卸载安全流程

```csharp
async Task UnloadPluginAsync(IWhiteboardWpfPlugin plugin, IPluginViewHandle handle,
                              CancellationTokenSource sessionCts)
{
    // 1. 取消 Session 的执行器和远端监听（防止 StopAsync 后晚到事件仍被处理）
    sessionCts.Cancel();

    // 2. 停止插件（停协同/Timer/动画/后台任务）
    await plugin.StopAsync(CancellationToken.None);

    // 3. UI 线程安全分离视图
    await Application.Current.Dispatcher.InvokeAsync(async () =>
        await handle.DetachAsync(CancellationToken.None));

    // 4. 释放插件（内部调 context.Unload()）
    await plugin.DisposeAsync();

    // 5. 强制 GC × 2 + WaitForPendingFinalizers
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    // 6. Debug 验证
    Debug.Assert(!_weakRefToContext.TryGetTarget(out _), "ALC unload leaked!");
}
```

---

## 六、富内容对象层级

```csharp
// Document Space 坐标系：逻辑单位，96dpi 无关，所有持久化数据均在此空间
// 屏幕坐标仅在渲染层（RenderTransform），绝不持久化

public abstract class WhiteboardObject
{
    public Guid    Id       { get; init; } = Guid.NewGuid();
    public Rect    Bounds   { get; private set; }   // Document Space，只通过命令修改
    public Matrix  Transform { get; private set; }  // 局部变换，只通过命令修改
    public int     ZIndex   { get; set; }
    public bool    IsLocked { get; set; }
    public string? OwnerId  { get; set; }           // 协同归属用户

    // Bounds/Transform 的修改必须通过 Session Executor 中的 Command 执行
    // 禁止外部代码直接 set，避免绕过 SpatialIndex 更新
    internal void ApplyTransform(Matrix delta) { Transform = delta * Transform; Bounds = ...; }
}

// Bounds/Transform 不暴露 setter，强制所有几何变更走 TransformCommand
// TransformCommand.Execute() 负责同步更新 SpatialIndex（Remove + Insert）

public class StrokeObject   : WhiteboardObject { public IReadOnlyList<PressurePoint> Points { get; init; } public StrokeStyle Style { get; init; } }
public class ImageObject    : WhiteboardObject { public string AssetId  { get; init; } = ""; /* SHA256[0..16] hex */ }
public class MindMapObject  : WhiteboardObject { public MindMapData  Data { get; set; } = default!; }
public class TableObject    : WhiteboardObject { public TableData    Data { get; set; } = default!; }
public class DocumentObject : WhiteboardObject { public string FileRef  { get; init; } = ""; }
public class TextObject     : WhiteboardObject { public string Content  { get; set; } = ""; public TextStyle Style { get; set; } = default!; }
```

### 空间索引（R-Tree）

```csharp
// 使用 RBush.NET，HitTest 和套索选择从 O(n) → O(log n)
// 只能在 Session Executor 内调用（已序列化，无需额外锁）
public sealed class SpatialIndex
{
    private readonly RBush<WhiteboardObject> _tree = new();

    public void Insert(WhiteboardObject obj) => _tree.Insert(MakeEnvelope(obj), obj);
    public void Remove(WhiteboardObject obj) => _tree.Remove(MakeEnvelope(obj), obj);
    // 对象 Bounds/Transform 变更时：Remove(old) → ApplyTransform → Insert(new)
    public void Update(WhiteboardObject obj, Action mutate)
    {
        _tree.Remove(MakeEnvelope(obj), obj);
        mutate();
        _tree.Insert(MakeEnvelope(obj), obj);
    }

    public IReadOnlyList<WhiteboardObject> HitTest(Point pt, double tolerance = 4.0)
        => _tree.Search(new Envelope(pt.X-tolerance, pt.Y-tolerance, pt.X+tolerance, pt.Y+tolerance));

    public IReadOnlyList<WhiteboardObject> LassoSelect(Rect r)
        => _tree.Search(new Envelope(r.Left, r.Top, r.Right, r.Bottom));
}
```

---

## 七、Command Pattern（含按对象 Undo/Redo）

```csharp
public interface IWhiteboardCommand
{
    IReadOnlyList<Guid> AffectedObjectIds { get; }

    // 是否支持对单个对象的独立撤销（单对象命令返回 true，批量命令返回 false）
    bool SupportsObjectUndo(Guid objectId);

    // 若 SupportsObjectUndo=true，生成仅针对该对象的逆命令
    IWhiteboardCommand? CreateObjectUndo(Guid objectId);

    void Execute();
    void Undo();
}

// 规则：批量命令（BatchTransformCommand.AffectedObjectIds.Count > 1）
// SupportsObjectUndo = false → UndoObject(id) 不会处理它，保护其他对象不受连带撤销
public class UndoRedoStack
{
    private readonly int                          _maxDepth;
    private readonly Stack<IWhiteboardCommand>    _undoStack = new();
    private readonly Stack<IWhiteboardCommand>    _redoStack = new();

    public UndoRedoStack(int maxDepth = 200) => _maxDepth = maxDepth;

    public void Execute(IWhiteboardCommand cmd)
    {
        cmd.Execute();
        _undoStack.Push(cmd);
        _redoStack.Clear();
        while (_undoStack.Count > _maxDepth) TrimOldest();
    }

    public void Undo() { if (_undoStack.TryPop(out var c)) { c.Undo();    _redoStack.Push(c); } }
    public void Redo() { if (_redoStack.TryPop(out var c)) { c.Execute(); _undoStack.Push(c); } }

    // 按对象撤销：仅处理 SupportsObjectUndo=true 的命令
    public bool UndoObject(Guid objectId)
    {
        var temp = new Stack<IWhiteboardCommand>();
        while (_undoStack.TryPop(out var cmd))
        {
            if (cmd.SupportsObjectUndo(objectId))
            {
                var inverse = cmd.CreateObjectUndo(objectId) ?? cmd;
                inverse.Undo();
                // 将 temp 按原序还原（先出后入，保持顺序）
                foreach (var t in temp.Reverse()) _undoStack.Push(t);
                return true;
            }
            temp.Push(cmd);
        }
        // 未找到：还原所有 temp
        foreach (var t in temp.Reverse()) _undoStack.Push(t);
        return false;
    }
}
```

---

## 八、OCR 策略模式

```csharp
public interface IOcrStrategy
{
    string   Name               { get; }
    bool     RequiresInternet   { get; }
    string[] SupportedLanguages { get; }
    Task<OcrResult> RecognizeAsync(
        IReadOnlyList<StrokeObject> strokes,
        string? languageHint,    // null = 由 Strategy 内部 Unicode Block 自动检测
        CancellationToken ct);
}

public record OcrResult(bool Success, string Text, float Confidence, IReadOnlyList<OcrWord> Words);

public sealed class OcrService
{
    private readonly Dictionary<string, IOcrStrategy> _strategies = new();
    // 使用 Volatile 保证 Activate/RecognizeAsync 并发安全（替代 lock）
    private volatile IOcrStrategy? _active;

    public void Register(IOcrStrategy strategy) => _strategies[strategy.Name] = strategy;

    public void Activate(string name)
    {
        if (!_strategies.TryGetValue(name, out var s))
            throw new KeyNotFoundException($"OCR strategy '{name}' not registered.");
        Volatile.Write(ref _active, s);
    }

    public bool IsAvailable => Volatile.Read(ref _active) != null;

    public Task<OcrResult> RecognizeAsync(
        IReadOnlyList<StrokeObject> strokes,
        string? languageHint = null,
        CancellationToken ct = default)
    {
        var strategy = Volatile.Read(ref _active);
        return strategy != null
            ? strategy.RecognizeAsync(strokes, languageHint ?? "", ct)
            : Task.FromResult(new OcrResult(false, "", 0f, []));
    }
}

// 内置策略（v1 默认 WindowsInk）
// 1. WindowsInkOcrStrategy  — Microsoft.Ink，本地，中/英
// 2. PaddleOcrStrategy      — 本地推理，多语言，需部署模型
// 3. AzureOcrStrategy       — 云端，精度最高，需联网
```

---

## 九、笔迹前向预测与延迟优化

### 延迟优化路线图

> 我们当前阶段目标是 L1，不要求做到最优层级，
> 但架构从一开始就预留了升级路径，确保后续可以逐级提升而无需重写核心。

#### 延迟层级总览

| 层级 | 技术手段 | 肉眼感知延迟 | 版本规划 |
|------|----------|-------------|---------|
| 基线 | 普通 `OnStylusMove` → UI 线程 → DrawingVisual | 20-40ms | — |
| **L1（当前目标）** | **双层 DrawingVisual + IStrokePredictor 前向预测** | **12-20ms** | **v1** |
| L2 | WetInkStylusPlugin（Stylus 专用线程） | 5-10ms | v2 |
| L3 | DirectComposition / WM_POINTER 原始消息 + 预测 | 2-7ms | v3（长期） |

---

#### L1：双层 DrawingVisual + 前向预测（v1，当前实施目标）

**原理**：用两个独立 `DrawingVisual` 层分离「已确认笔迹」和「预测笔迹」，
`IStrokePredictor` 用最近 5-8 个点预测未来 12-24ms 内的轨迹，在下一帧到来前填补视觉空白。

```
输入采样 (8-16ms) ──▶ OnStylusMove (UI线程)
                           │
                    ┌──────▼──────┐
                    │ ConfirmedLayer  │  永久叠加，不清除
                    └──────────────┘
                    ┌──────▼──────┐
                    │ PredictedLayer │  每帧 RenderOpen() 清除重绘
                    └──────────────┘
                           │
                  IStrokePredictor.Predict()
                  lookaheadMs = 16f（自适应 12~24ms）
```

**需要做的工作**：
- `InkSurface`：双层 `DrawingVisual`（ConfirmedLayer + PredictedLayer）
- `IStrokePredictor` + `InkForecast`：线性外推（落笔前 3 点）+ Kalman 滤波（速度稳定后）
- 抬笔/急转时立即清除预测层（`Confidence = 0`）
- 使用 `StylusEventArgs`（仍在 UI 线程，不换线程模型）

**局限**：UI 线程仍会被 WPF 帧调度轻微阻塞，GC 暂停可导致偶发抖动（.NET 10 DATAS GC 已大幅缓解）。

---

#### L2：WetInkStylusPlugin Stylus 专用线程（v2）

**原理**：WPF `StylusPlugIn` 在独立的 Stylus 线程上触发，优先级高于 UI 线程，
可在 UI 线程忙碌时继续采样。通过 `Channel<RawPoint>` 将点传回 UI 线程渲染。

```
触笔硬件 ──▶ Stylus 线程 (WetInkStylusPlugin)
                  │  TryWrite → Channel<RawPoint>(bounded 128)
                  ▼
            UI 线程消费 Channel
                  │
            DrawingVisual 渲染（Confirmed + Predicted）
```

**与 L1 的差别**：仅需在 `InkSurface` 中添加 `StylusPlugIn`，核心渲染逻辑不变。
架构上 `IStrokePredictor` 接口不变，L1 → L2 升级成本极低（约 1-2 天）。

**需要做的工作**（v2 阶段）：
- 实现 `WetInkStylusPlugin : StylusPlugIn`，有界 Channel，`DropOldest` 防阻塞
- `InkSurface` 注册 `StylusPlugin`，消费 Channel 驱动渲染
- 通过 `SessionCapability.PredictiveInk` 开关控制是否激活

**进入条件**：L1 延迟实测 > 15ms，或用户反馈书写抖动明显。

---

#### L3：DirectComposition / WM_POINTER 原始消息（v3，长期方向）

**原理**：绕过 WPF 消息队列，直接使用 Win32 `WM_POINTER` 消息（触控采样率可达 250Hz），
渲染交给 `Windows.UI.Composition`（OS 合成器独立线程，不受 WPF 帧率影响）。

```
触笔硬件 (250Hz) ──▶ WM_POINTER (Win32)
                          │  PInvoke / HwndSource
                    预测线程 (独立)
                          │
               DirectComposition 合成器线程
                          │
                    屏幕输出（绕过 WPF 渲染树）
```

**需要做的工作**（v3 阶段）：
- `HwndSource` 接入 Win32 `WM_POINTER` 消息
- 将 Confirmed 笔迹输出到 `SpriteVisual`（WinRT Composition）
- Predicted 层改用独立合成器 Visual，每帧原地替换
- 需要 Win32 Interop 层（`Microsoft.Graphics.Canvas` 或纯 D2D PInvoke）

**何时考虑**：面向高端硬件（Surface Pro / 专业触控屏）且 L2 仍不满足的场景。
L3 改造量较大，建议作为独立功能分支，不影响主线 v1/v2 交付。

**进入条件**：L2 延迟实测 > 8ms，且用户硬件支持 250Hz 触控采样率。

### StylusPlugin 线程模型（L2 设计备用）

```csharp
public sealed class WetInkStylusPlugin : StylusPlugIn
{
    private readonly Channel<RawPoint> _pipe =
        Channel.CreateBounded<RawPoint>(new BoundedChannelOptions(128)
            { FullMode = BoundedChannelFullMode.DropOldest });

    protected override void OnStylusDown(RawStylusInput input, bool targetVerified)
        => EnqueuePoints(input);
    protected override void OnStylusMove(RawStylusInput input, bool targetVerified)
        => EnqueuePoints(input);

    private void EnqueuePoints(RawStylusInput input)
    {
        foreach (var pt in input.GetStylusPoints())
            _pipe.Writer.TryWrite(new RawPoint(pt.X, pt.Y, pt.PressureFactor));
    }

    public IAsyncEnumerable<RawPoint> ReadAllAsync(CancellationToken ct)
        => _pipe.Reader.ReadAllAsync(ct);
}
```

### 双层渲染 + 预测接口

```csharp
public interface IStrokePredictor
{
    /// <param name="recent">最近 5~8 个确认点（Document Space）</param>
    /// <param name="lookaheadMs">预测窗口；自适应 12~24ms，硬上限 32ms</param>
    /// <param name="isStrokeBegin">落笔第一帧，强制线性外推</param>
    IReadOnlyList<PredictedPoint> Predict(
        IReadOnlyList<PressurePoint> recent,
        float lookaheadMs = 16f,
        bool isStrokeBegin = false);
}

public record PredictedPoint(float X, float Y, float Pressure, float Confidence);

// 算法策略（InkForecast 内嵌）：
// isStrokeBegin=true 或前 3 点 → 线性外推
// 速度稳定 → Kalman 滤波
// 急减速/方向急转/抬笔 → Confidence=0，清除预测层
// lookaheadMs 自适应：慢速 12ms，快速 24ms，上限 32ms
```

---

## 十、保存格式规范

### 文件容器
- 扩展名：`.wbd`（实为 ZIP）
- 原子保存：写临时文件 → fsync/Close → 替换目标

### manifest.json 结构（完整定义）

```json
{
  "formatVersion": 1,
  "documentId": "uuid-v4",
  "generatorVersion": "1.0.0",
  "createdUtc": "2026-06-09T00:00:00Z",
  "modifiedUtc": "2026-06-09T12:00:00Z",
  "pageCount": 3,
  "featureFlags": ["MultiPage", "PredictiveInk"],
  "pages": [
    { "id": "page_001", "title": "第 1 页", "background": "white" }
  ],
  "assets": [
    { "id": "a3f4b8c1d2e5f601", "mimeType": "image/webp", "refCount": 2, "sizeBytes": 102400 }
  ]
}
```

**资产 refCount 管理**：`refCount` 在保存时从对象图重新计算（派生值，非运行时状态），
每次保存都做一次全量扫描，refCount=0 的 entry 在 ZIP 中删除（惰性 GC on save）。

### 目录结构

```
document.wbd (ZIP)
├── manifest.json
├── pages/
│   ├── page_001/
│   │   ├── meta.json        ← 页面标题、背景色
│   │   ├── page.bin         ← 所有对象（改名自 strokes.bin，含类型分派）
│   │   └── thumb.webp       ← 128px 缩略图
│   └── page_002/
└── assets/
    └── img_[sha256-16hex].webp
```

### page.bin 格式（通用对象块）

```
[文件头]  22 bytes
  Magic:         4 bytes   "WBPG"（原"WBSK"已废弃）
  FormatVersion: 2 bytes   当前 = 1
  FeatureFlags:  4 bytes   预留
  ObjectCount:   4 bytes   本页对象总数（含所有类型）
  HeaderCRC32:   4 bytes   文件头校验
  Reserved:      4 bytes

[对象块] × ObjectCount
  ChunkLength:   4 bytes   整块字节数（可跳过损坏块）
  ObjectKind:    2 bytes   见下方枚举
  SchemaVersion: 2 bytes   本 Kind 的 payload schema 版本
  ObjectId:     16 bytes   Guid
  ZIndex:        4 bytes   int32
  Bounds:       16 bytes   X,Y,W,H float32 × 4（Document Space）
  Transform:    24 bytes   Matrix3x2（6 × float32）
  IsLocked:      1 byte    bool
  Reserved:      3 bytes
  Payload:      ChunkLength - 固定头大小  bytes  ← Kind 专属数据（见下）
  ChunkCRC32:    4 bytes   本块校验
```

### ObjectKind 枚举与 Payload 定义

```csharp
public enum ObjectKind : ushort
{
    Stroke   = 1,
    Image    = 2,
    Text     = 3,
    MindMap  = 4,
    Table    = 5,
    Document = 6,
}
```

| Kind | Payload 格式 |
|------|-------------|
| Stroke | StyleFlags(4) + ColorARGB(4) + Thickness(float32) + PointCount(4) + Points[N×12: X,Y,P float32] |
| Image | AssetIdBytes(16, SHA256[0..16]) |
| Text | UTF-8 编码内容长度(4) + UTF-8 字节 + TextStyle JSON(UTF-8) |
| MindMap | JSON 序列化 MindMapData（UTF-8，长度前缀） |
| Table | JSON 序列化 TableData（UTF-8，长度前缀） |
| Document | UTF-8 文件引用路径长度(4) + UTF-8 路径字节 |

### StyleFlags 位布局（Stroke 专用）

```
Bits  0- 3 : PenType    (0=Pen,1=Highlighter,2=Marker,3=Eraser)
Bits  4- 7 : InkType    (0=Normal,1=Calligraphy,2=Airbrush)
Bit   8    : IsFilled
Bit   9    : IsAntiAlias
Bits 10-15 : Reserved（写 0，读时忽略）
```

**实现要求**：
- `BinaryPrimitives` + `Span<byte>` 手写序列化
- CRC32 失败的块跳过而非中断
- `manifest.json` 的 `formatVersion` 驱动 MigratorChain

---

## 十一、会话能力模型

```csharp
[Flags]
public enum SessionCapability
{
    None          = 0,
    Save          = 1 << 0,
    Collaboration = 1 << 1,
    OCR           = 1 << 2,
    PredictiveInk = 1 << 3,
    Export        = 1 << 4,
    MultiPage     = 1 << 5,
}

public enum SessionRole { Solo = 0, Presenter = 1, Participant = 2 }
```

---

## 十二、架构缺口与推荐方案（完整 26 项）

| # | 缺口 | 推荐方案 | Phase |
|---|------|----------|-------|
| 1 | 晚加入/断线重连 | 服务端快照 + SeqNo 重放 | v1.1 |
| 2 | 权限模型 | SessionRole + Operation 权限 Mask | v1.1 |
| 3 | ZIP Bomb/路径穿越 | Entry 路径白名单校验 + 大小上限 | v1.0 |
| 4 | 遥测 | IHostTelemetry 记录 Bus 延迟/丢弃/RMSE/ALC 泄漏 | v1.1 |
| 5 | 操作回放测试 | 黄金文件测试 | v1.0 |
| 6 | ALC 泄漏浸泡测试 | 1000 次循环 + WeakReference | v1.0 |
| 7 | Document Space 转换 | CoordinateConverter 集中封装 | v1 |
| 8 | 图片 SHA256 去重 | refCount 在保存时从对象图重算 | v1.0 |
| 9 | 多对象变换 | BatchTransformCommand（SupportsObjectUndo=false） | v1.0 |
| 10 | MindMap/Table 内部 Undo | 对象内部 UndoRedoStack + 外部快照 | v1.1 |
| 11 | StyleFlags 扩展性 | Reserved bits + FormatVersion 迁移 | v1 |
| 12 | OCR 语言自动检测 | languageHint=null，策略内部 Unicode Block | v1.1 |
| 13 | 协同冲突 v1 | Last-Write-Wins（ServerSeqNo 大的胜出） | v1.0 |
| 14 | 预测自适应 | 速度驱动 12-24ms，上限 32ms | v1.0 |
| 15 | StylusPlugin 可选 | SessionCapability.PredictiveInk 控制 | v2 |
| 16 | .NET 版本 | net10.0-windows（Dynamic PGO + DATAS GC） | v1 |
| 17 | 主题资源边界 | 插件自包含，仅读 AccentColor | v1.0 |
| 18 | 快捷键冲突 | IHostShortcuts.Register，宿主仲裁 | v1.0 |
| 19 | 序列化版本兼容 | MigratorChain 由 manifest.formatVersion 驱动 | v1.0 |
| 20 | Contracts TFM 拆分 | Contracts(netstandard2.0) + Contracts.Wpf(net10.0-windows) | v1.0 ✅ |
| 21 | 非 Stroke 对象持久化 | page.bin 通用 ObjectKind 分派 | v1.0 ✅ |
| 22 | UndoObject 多对象命令语义 | SupportsObjectUndo + CreateObjectUndo 接口 | v1.0 ✅ |
| 23 | Session 并发写竞争 | Channel Executor 序列化所有变更 | v1.0 ✅ |
| 24 | PluginMessage Payload 生命周期 | Payload 改 ReadOnlyMemory<byte>（ContentType 注明） | v1.0 ✅ |
| 25 | 订阅句柄/退订语义 | IPluginSubscription（含 IAsyncDisposable） | v1.0 ✅ |
| 26 | InkForecast 依赖清单 | 嵌入前做依赖盘点，确认无 native 依赖（ONNX 等） | Phase 0 |

---

## 十三、实施阶段规划

### Phase 0：脚手架（1~2 天）
- [ ] 建 Solution，创建 6 个项目骨架（含 WhiteBoard.Contracts.Wpf）
- [ ] 定义全部 Contracts / Contracts.Wpf 接口（不实现）
- [ ] 定义 Protocol DTOs（含 ObjectKind、manifest.json 结构体）
- [ ] InkForecast 依赖清单盘点（确认无 native 依赖）
- [ ] 建 tests 目录

### Phase 1：Core 领域层（3~5 天）
- [ ] WhiteboardObject 层级（6 子类，Bounds/Transform 私有 setter）
- [ ] WhiteboardSession + Channel Executor
- [ ] UndoRedoStack（含 SupportsObjectUndo/CreateObjectUndo）
- [ ] PageModel、PageManager
- [ ] SpatialIndex（RBush.NET，Update 原子 Remove+mutate+Insert）
- [ ] PageSerializer（page.bin，ObjectKind 分派，BinaryPrimitives）
- [ ] OcrService（Volatile 保护 + WindowsInkOcrStrategy）
- [ ] Core.Tests：UndoRedo、UndoObject、序列化黄金文件

### Phase 2：宿主演示（2~3 天）
- [ ] WhiteboardPluginContext（ALC，含 Contracts.Wpf 到共享列表）
- [ ] PluginManager（Load/Unload + 严格生命周期状态机）
- [ ] MockCollaborationAdapter
- [ ] LocalSaveService（含 ZIP 安全校验 + manifest refCount 重算）
- [ ] Host.Demo 验证热加载/卸载
- [ ] ALC 泄漏浸泡测试（1000 次循环）

### Phase 3：UI 层（5~8 天）
- [ ] InkSurface（双层 DrawingVisual，Document Space 坐标系）
- [ ] CoordinateConverter（屏幕 ↔ Document Space）
- [ ] 从 InkCanvasRe.cs 移植核心渲染逻辑
- [ ] InkToolbar、PageStrip
- [ ] WhiteboardViewModel
- [ ] WhiteboardPlugin（实现 IWhiteboardWpfPlugin）

### Phase 4：预测集成（2~3 天）
- [ ] InkForecast 内嵌：LinearPredictor、KalmanPredictor
- [ ] IStrokePredictor 自适应 lookaheadMs
- [ ] PredictedInkRenderer 集成
- [ ] 延迟基准测量（L1 目标 12-20ms）

### Phase 5：协同接入（3~5 天）
- [ ] SignalRCollaborationAdapter
- [ ] LWW 服务端序号协议
- [ ] 晚加入/重连快照流程
- [ ] 协同操作回放黄金文件测试

---

## 十四、技术决策记录（ADR）

| 编号 | 决策 | 原因 |
|------|------|------|
| ADR-001 | `isCollectible: true` | 热卸载硬需求 |
| ADR-002 | 共享程序集精确 HashSet 匹配 | 防 System.* 通配导致类型冲突 |
| ADR-003 | `IAsyncEnumerable` 替代 Rx | 简单，Channel 背压可控 |
| ADR-004 | 服务端序号替代 CRDT | v1 复杂度可控 |
| ADR-005 | Undo/Redo 为本地行为 | 避免分布式 Undo 复杂性 |
| ADR-006 | Contracts 两层分离 | 插件 API 与数据协议独立演进 |
| ADR-007 | ZIP 容器 + 帧结构二进制 | 可调试 + 局部损坏可恢复 |
| ADR-008 | 预测上限 32ms / 自适应 12-24ms | 超 32ms 转角过冲明显 |
| ADR-009 | Document Space 规范坐标系 | 96dpi 无关，跨设备一致 |
| ADR-010 | StyleFlags 位布局（Color/Thickness 独立字段） | 精度需求，不适合压位 |
| ADR-011 | R-Tree（RBush.NET）空间索引 | HitTest/套索 O(log n)，无 native 依赖 |
| ADR-012 | OCR 策略模式 | 按需切换在线/离线策略 |
| ADR-013 | v1 冲突：Last-Write-Wins | ServerSeqNo 大的胜出，v2 升级 OT |
| ADR-014 | UndoRedo maxDepth=200 | 内存与历史深度平衡 |
| ADR-015 | InkForecast v1 内嵌 Plugin | 避免跨 ALC 类型问题 |
| ADR-016 | net10.0-windows | Dynamic PGO + DATAS GC，抖动降低 40-60% |
| ADR-017 | Contracts 拆为 std2.0 + net10-win 两层 | FrameworkElement 是 WPF 类型，netstandard2.0 不包含 |
| ADR-018 | page.bin 通用 ObjectKind 块（废弃 strokes.bin） | 非 Stroke 对象无法持久化的根本修复 |
| ADR-019 | SupportsObjectUndo + CreateObjectUndo | 多对象命令不能安全按单对象撤销，需显式区分 |
| ADR-020 | Channel Executor 序列化所有 Session 变更 | 消除 R-Tree/UndoStack/PageModel 并发写竞争 |
| ADR-021 | Payload 改 ReadOnlyMemory<byte> | JsonElement 生命周期依赖 JsonDocument，跨异步边界不安全 |
| ADR-022 | IPluginSubscription 显式退订句柄 | IAsyncEnumerable 独自无法保证退订语义 |

---

## 十五、参考资源

- Codex 审查会话：`SESSION_ID: 019eaa01-7204-7573-bc60-5dd0e1439aec`
- 原始白板代码：`D:\CompanyProject\client_win\Controls.Ink\`
- InkForecast：`E:\Demo\demo\SignalR Demo\SynergyDemo\InkForecast\`
- .NET AssemblyLoadContext 可卸载性：https://learn.microsoft.com/dotnet/standard/assembly/unloadability
- RBush.NET：https://github.com/mourner/rbush
- JsonElement 生命周期与 Clone()：https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/use-dom
- WPF 目标框架要求：https://learn.microsoft.com/dotnet/core/tools/sdk-errors/netsdk1136

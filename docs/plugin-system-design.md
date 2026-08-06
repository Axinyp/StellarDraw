# Plugin 系统设计评审

## 1. 评审结论与范围

本文定义 Axinyp Plugin 从当前启动期模型演进到未来运行时扩展模型的设计边界。本文是设计文档，不是 runtime hot-load/hot-unload 的实现计划；在本设计获得评审批准前，不修改现有 Plugin runtime、Manifest 或 WPF catalog API。

当前必须保持的产品边界：

- Plugin 代码被视为**受信任代码**；`AssemblyLoadContext` 只提供程序集依赖隔离，不是安全 sandbox；
- 当前模型只支持宿主构建前发现、启动期加载和模块装配；
- 每个插件使用独立的 collectible `AssemblyLoadContext`，但插件仍运行在宿主进程权限内；
- Plugin 模块的生命周期跟随宿主，Host 停止并释放时按反向顺序清理并调用 `Unload()`；
- 当前不承诺运行时热加载、热卸载、插件商店、插件停用界面或运行时 UI 动态增删；
- legacy 白板 `PluginManager` / `IWhiteboardWpfPlugin` 与 Axinyp startup plugin 分离，不得通过 bridge 伪装成 `IModule`。

未来运行时插件能力必须作为独立能力设计，不能把启动期 loader 的 `Dispose()` 直接包装成热卸载 API。

## 2. 当前实现基线

当前启动期流程为：

1. 扫描插件根目录下的插件子目录；
2. 读取并校验 `plugin.json`；
3. 检查重复 ID、缺失依赖和依赖环，并按依赖顺序排列；
4. 为每个插件创建 collectible `StartupPluginLoadContext`；
5. 通过入口程序集发现公开 `IModule` 类型并创建模块实例；
6. 将模块回灌到宿主的 Build 前模块装配流程；
7. Host Dispose 时反向释放插件句柄并调用 `AssemblyLoadContext.Unload()`。

当前公共 `PluginManifest` 只有 `Id`、`Version`、`EntryAssembly` 和插件 ID 字符串列表 `Dependencies`。当前 `StartupPluginLoadContext` 会共享 Abstractions 以及显式配置的 Microsoft/宿主契约程序集；实现包不应作为默认共享边界。

当前运行时不保存“可重新加载”的插件状态机，也没有对真实 DLL fixture、程序集解析、ALC 弱引用回收或 WPF 引用链清理建立完整验收。因此，当前测试通过只能证明启动期发现和安全跳过失败插件，不代表插件可以运行时卸载。

## 3. Failure taxonomy：当前能力与目标

### 3.1 当前可观察的公共分类

现有 `PluginLoadFailureKind` 已能区分以下类别：

- `ManifestMissing` / `ManifestInvalid`；
- `DuplicatePluginId`；
- `MissingDependency` / `DependencyCycle` / `DependencyFailed`；
- `EntryAssemblyMissing`；
- `AssemblyLoadFailed`；
- `ModuleDiscoveryFailed`；
- `ModuleActivationFailed`。

公共 `PluginLoadFailure` 和 metadata 只保留安全消息、诊断上下文、异常类型名等值类型或字符串信息；原始 `Exception` 只交给 `ILogger`，不能进入 catalog、UI 或长期缓存。

### 3.2 已确认的技术债

`GetExportedTypes()` 失败、空模块结果与 `Activator.CreateInstance()` 失败已分别产出
`ModuleDiscoveryFailed` / `ModuleActivationFailed`（2026-08 起）。下列根因仍被折叠为
`AssemblyLoadFailed` 或缺少稳定、可独立统计的诊断语义：

- managed assembly resolution failure；
- native dependency resolution failure；
- module registration failure；
- module `InitializeAsync` / `StartAsync` lifecycle failure。

在修改实现前，目标 failure taxonomy 应按下面的阶段固定诊断代码和 EventId：

| 阶段 | 目标类别 | 说明 |
| --- | --- | --- |
| Manifest | `ManifestMissing` / `ManifestInvalid` / `ManifestUnsupported` | 文件、JSON、字段、schemaVersion 或未知字段策略失败 |
| Identity | `PluginIdentityInvalid` / `DuplicatePluginId` / `VersionInvalid` | ID 规范化、SemVer、重复身份失败 |
| Dependency | `MissingDependency` / `DependencyVersionMismatch` / `DependencyCycle` / `DependencyFailed` | 依赖存在性、版本范围和传播失败 |
| Entry | `EntryAssemblyMissing` / `EntryAssemblyInvalid` | 路径越界、扩展名、文件身份或入口声明失败 |
| Resolution | `ManagedAssemblyResolutionFailed` / `NativeAssemblyResolutionFailed` / `SharedAssemblyConflict` | 解析来源、共享身份或 native 冲突失败 |
| Discovery | `ModuleDiscoveryFailed` / `NoModuleFound` | `GetExportedTypes()` 或有效模块为空 |
| Activation | `ModuleActivationFailed` | 构造、依赖注入或实例化失败 |
| Registration | `ModuleRegistrationFailed` | 模块回灌宿主 catalog/容器失败 |
| Lifecycle | `ModuleLifecycleFailed` | 初始化、启动、停止或释放阶段失败 |

目标 taxonomy 不要求一次性扩展所有公共 enum；必须先保证每个公开诊断代码稳定、日志事件可关联、原始异常不进入公共对象，并对兼容旧消费者提供明确映射策略。任何兼容映射都不得让 UI 误以为不同根因已经相同。

## 4. Future `plugin.json` schema

未来 schema 建议使用明确版本号和严格边界。示例：

```json
{
  "schemaVersion": 1,
  "id": "axinyp.example.editor",
  "version": "1.2.0",
  "entryAssembly": "Axinyp.Example.Editor.dll",
  "dependencies": [
    {
      "id": "axinyp.example.shared",
      "versionRange": ">=1.0.0 <2.0.0"
    }
  ],
  "hostApi": {
    "versionRange": ">=1.0.0 <2.0.0"
  },
  "metadata": {
    "source": "internal",
    "build": "2026.08.03.1",
    "integrity": {
      "algorithm": "SHA-256",
      "value": "..."
    }
  }
}
```

字段规则：

- `schemaVersion` 必须是受支持的正整数；未知 schema 版本拒绝加载，并返回稳定的 `ManifestUnsupported` 诊断；
- `id` 使用规范化的小写、稳定前缀和点分段格式；比较时按规范化值处理，日志保留安全的规范化 ID；
- `version` 和依赖 `versionRange` 使用明确的 SemVer 语义，禁止将任意字符串比较当作版本排序；
- `entryAssembly` 只能是插件目录内的相对 managed assembly 文件名，禁止绝对路径、`..` 越界和隐式搜索；
- `dependencies` 使用对象表达版本范围，而不再只表达插件 ID；依赖解析必须同时验证 ID 和范围；
- `hostApi.versionRange` 表示插件所需的宿主公共 API 兼容范围，只针对稳定 Abstractions 契约，不代表实现包 ABI 或 WPF 内部类型稳定；
- `metadata` 中的 source、build、integrity 均为可选部署卫生信息，不授予插件更高权限，也不等于 sandbox 或代码签名验证；
- 完整性校验和 allowlist 只用于受信任部署的来源、版本和文件一致性检查，不能宣称可以阻止恶意代码执行。

未知字段和弃用策略：

- schema v1 默认拒绝未知顶层字段，避免拼写错误被静默接受；允许的扩展字段必须进入后续 schema 版本或显式命名空间；
- 可选 metadata 内部字段可采用“忽略并记录诊断 warning”的兼容策略，但不能改变加载语义；
- 被弃用字段至少保留一个 schema 主版本的 warning 周期，随后以 `ManifestDeprecatedField` 或等价稳定代码拒绝；
- schema、host API、插件版本和依赖范围不兼容时，必须分别给出诊断代码和安全上下文，不得统一伪装为入口程序集加载失败；
- 错误消息不包含凭据、任意参数、原始异常文本或插件内部对象的 `ToString()`。

## 5. Shared assembly 策略

共享程序集的目标是保持契约类型身份一致，而不是让插件复用整个宿主实现层。

### 5.1 共享白名单

默认候选共享边界为：

- `Axinyp.Wpf.Hosting.Abstractions`；
- 经明确 allowlist 批准、且确实需要统一类型身份的 Microsoft abstractions；
- 由宿主显式配置并经过身份校验的附加契约程序集。

`Axinyp.Wpf.Hosting`、`Axinyp.Wpf.Hosting.Wpf` 及业务实现程序集不应默认共享。插件自己的实现依赖应由插件目录和自己的 resolver 管理。

### 5.2 身份与解析规则

每个共享候选必须按 assembly simple name、版本、culture 和 public key token（存在时）验证完整身份。仅按 simple name 命中不足以证明类型兼容；身份不一致时应以 `SharedAssemblyConflict` 失败，不得静默返回宿主版本。

managed 与 native 解析必须分开处理：

- managed resolver 只解析托管程序集，并记录请求名、候选路径、最终来源和共享命中结果；
- native resolver 只解析 native library，并记录平台、架构、候选路径和最终来源；
- managed/native 同名不应相互兜底；解析冲突、架构不匹配和来源越界必须失败；
- 诊断 tracing 只写安全的程序集身份和规范化路径，不写 token、密钥或插件业务数据；
- resolver 不得从插件目录外任意回退搜索，也不得将未知实现程序集加入共享表来“修复”类型转换。

## 6. Future runtime lifecycle

运行时扩展只能在单独的 runtime plugin API、状态机和 catalog snapshot 机制批准后实现。目标生命周期如下：

```text
Discovered
  -> Loaded
  -> Started
  -> StopRequested
  -> WorkStopped
  -> ViewsDetached
  -> ProvidersDisposed
  -> ModulesDisposed
  -> ReferencesCleared
  -> Unloaded
  -> Collected
```

卸载事务必须按以下反向依赖顺序执行：

1. 将插件标记为 `StopRequested`，拒绝新的导航、贡献和命令入口；
2. 取消插件拥有的 cancellation token，并等待其后台任务结束；
3. 在 Dispatcher 仍可用时移除 WPF views、resources、bindings、commands、events 和 navigation journal 中的插件引用；
4. 停止并释放 providers、模块、插件专属 scopes 和其他宿主注册；
5. 清空 catalog、导航、dispatcher callback 和诊断缓存中的插件引用；
6. 以反向装配顺序调用 `Dispose` / `DisposeAsync`；异常必须聚合并继续执行后续清理；
7. 调用 `AssemblyLoadContext.Unload()`；
8. 仅在所有强引用清除后，用 `WeakReference` 和受控 GC 回收验证确认 ALC 可收集。

幂等和异常规则：

- Stop、detach、dispose 和 unload 每一步必须可重复调用；重复调用不得重新注册资源或抛出无意义的状态异常；
- 一步失败不能跳过后续清理；返回值应包含阶段分类，日志保留完整异常；
- 不能在持有 UI 锁或同步阻塞 UI Dispatcher 时等待插件任务；所有异步工作必须可取消、可等待；
- Dispatcher 正在关闭时，不得再排队新的 UI detach；应根据可用性执行安全的非视觉引用清理，并记录 `DispatcherUnavailable` 类诊断；
- 若插件任务无法在规定期限结束，不能强行宣称 ALC 已卸载；必须报告超时和残留引用诊断，由宿主策略决定是否终止整个进程。

## 7. WPF Contribution ALC hygiene

WPF catalog 的长期对象只能保存宿主可理解的不可变元数据，例如稳定 ID、排序、显示文本键、命令键、导航目标的宿主契约标识和安全诊断代码。Catalog/Shell 不得长期保存下列插件强引用：

- Provider 实例或 provider scope；
- 插件 `Type`、插件 assembly、`MethodInfo` 或 delegate 闭包；
- 原始 `Exception`、异常对象图或包含插件对象的诊断 payload；
- `ResourceDictionary`、binding、binding source、`DependencyObject` 或视觉树；
- `ICommand` 实例、事件处理器、timer、dispatcher callback 或消息 recipient；
- 带插件 View/VM 实例的 navigation journal、缓存页面或窗口 owner 链。

Catalog freeze 时机：

1. 所有启动期 Provider 在独立 scope 内执行；
2. Provider 输出经过 schema、ID、排序和冲突校验；
3. 将输出转换为纯宿主元数据 snapshot；
4. 释放 Provider scope；
5. Host 启动前冻结 snapshot，运行期间只读。

未来 runtime refresh 的前置条件：

- 插件已经停止接受新的工作，且所有未完成任务已取消并等待结束；
- Shell 已停止创建该插件的新 View、Dialog、Command 和导航项；
- 旧 snapshot 中的贡献已从视觉树、资源、binding、事件和 journal 脱离；
- 新旧 snapshot 通过版本、ID、冲突和 host API 兼容性验证；
- refresh 失败时旧 snapshot 仍可安全使用，不能先清空旧 catalog 再尝试构造新结果；
- refresh 完成后才释放旧 provider scope 和旧插件引用；
- 所有步骤均经过目标 Dispatcher，Dispatcher 关闭时拒绝 refresh，并保留旧状态或进入明确失败状态。

## 8. Future test matrix

运行时能力批准后，至少需要以下测试矩阵；当前只允许先补测试 fixture 和设计，不得提前新增 runtime API：

| 维度 | 必测场景 | 验收重点 |
| --- | --- | --- |
| DLL fixture | 真实插件 DLL、多个模块、空模块、错误入口类型 | 不依赖 mock 反射结果；失败分类稳定 |
| Manifest | 缺失、非法 JSON、未知字段、schema 版本、弃用字段 | 安全诊断、兼容策略、无异常对象泄露 |
| Identity | ID 规范化、重复 ID、非法 SemVer、host API 不兼容 | 比较规则和诊断代码一致 |
| Dependency | 缺失、范围不满足、环、失败传播、多级依赖 | 依赖错误不误报为 assembly 错误 |
| Managed resolver | 插件私有依赖、缺失依赖、路径越界、来源 tracing | resolver 来源明确且不越界 |
| Native resolver | 架构不匹配、缺失库、同名冲突 | managed/native 不互相兜底 |
| Shared identity | 共享成功、版本冲突、public key 冲突、非白名单程序集 | 类型身份唯一，冲突显式失败 |
| Discovery/activation | `GetExportedTypes()` 失败、无模块、构造失败、DI 失败 | 分别映射 discovery/activation |
| Registration/lifecycle | 模块注册失败、Initialize/Start/Stop 失败、多模块顺序 | 失败传播、反向清理、异常聚合 |
| Runtime unload | Stop → cancel/await → detach → dispose → clear → unload | 幂等、反向顺序、无跳步 |
| ALC collection | `WeakReference`、受控 GC、卸载后 reload | ALC、程序集和模块均可收集；旧实例不复活 |
| WPF hygiene | View、ResourceDictionary、binding、command、event、journal 清理 | 无插件强引用残留 |
| Dispatcher closing | 关闭前、检查后执行前关闭、关闭中 refresh/unload | 不排队新 UI 工作，scope 仍释放 |
| Refresh rollback | 新 snapshot 构造/校验失败、旧 snapshot 恢复 | 不出现半更新或空 catalog |
| Reload isolation | 卸载后再次加载同版本/新版本、旧 static state | 旧 static、缓存和事件不影响新实例 |
| Security boundary | 受信任插件说明、allowlist/integrity、权限失败 | 不把 ALC 或完整性检查描述为 sandbox |

## 9. 审批门

在本设计经用户评审批准前，以下内容保持冻结：

- `PluginManifest`；
- `StartupPluginDiscoverer`；
- `StartupPluginRuntime`；
- `StartupPluginLoadContext`；
- WPF contribution catalog 的 runtime refresh；
- 任何新增 runtime load/unload、plugin stop 或 hot-reload 公共 API。

当前交付只包括文档边界和后续验收标准。下一阶段若要实现，必须先把 schema、failure taxonomy、shared assembly identity、runtime lifecycle 和 WPF hygiene 分解成可独立回归的原子变更，并继续由真实 consumer 验证，而不是以白板 legacy Plugin 的行为代替框架验证。

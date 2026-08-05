# 架构说明

## 总体分层

- `Axinyp.Wpf.Hosting.Abstractions`：稳定公共契约层。
- `Axinyp.Wpf.Hosting`：Generic Host、模块、生命周期、启动期插件、设置、本地化默认实现。
- `Axinyp.Wpf.Hosting.Wpf`：Window、Region、Navigation、Dialog、Theme、WPF 本地化与 UI Contribution 默认实现。
- `samples/*`：可运行样例，用来展示最小接入方式。
- `tests/*`：核心与 WPF 验证，用来保护抽象边界和默认行为。
- `consumer-smoke/*`：本地包消费者验证项目，用来模拟源码仓库外部消费者。

## 为什么要有 Abstractions

框架拆分后的核心目标不是“多一个项目”，而是明确类型身份边界。

如果公共接口同时存在于：

- `Axinyp.Wpf.Hosting`
- `Axinyp.Wpf.Hosting.Wpf`
- 插件程序集自己的引用副本

那么就会出现典型问题：

1. 名字一样，但 CLR 看来不是同一个类型；
2. 插件里的 `IModule` / `IViewLocator` / `IWpfUiContributionProvider` 无法和宿主侧强制转换；
3. 测试、样例、消费者项目会在不同实现包之间产生重复依赖；
4. 即使只做本地包消费，不做线上发布，这类类型冲突也会在外部恢复与编译时被放大，排查成本高于源码期。

因此当前架构要求：

- **所有稳定公共契约只保留在 `Axinyp.Wpf.Hosting.Abstractions`**；
- `Axinyp.Wpf.Hosting` 与 `Axinyp.Wpf.Hosting.Wpf` 只保留默认实现和实现专属辅助类型；
- 插件共享程序集时，优先共享 Abstractions，而不是整个实现包。

## 多目标策略

`Axinyp.Wpf.Hosting.Abstractions` 采用多目标：

- `net10.0`
- `net10.0-windows`

这样做的原因是：

- 核心宿主能力不应被 `System.Windows*` 绑定污染；
- WPF 公共契约又确实需要 `Window`、`FrameworkElement`、`ContentControl` 等类型；
- 保持一个统一的包名，比再拆第二个 `*.Abstractions.Wpf` 更容易让消费者理解和引用。

具体规则：

- `net10.0` 只包含核心抽象；
- `net10.0-windows` 同时包含核心抽象与 WPF 抽象；
- WPF 类型不通过核心全局 using 暴露，而是在对应契约文件内局部声明依赖。

## 各层职责边界

### Axinyp.Wpf.Hosting.Abstractions

负责：

- 模块契约；
- 生命周期契约；
- 启动任务契约；
- 插件发现结果契约；
- 设置/本地化抽象；
- WPF 表示层公共契约；
- 主题与 UI Contribution 元数据契约。

不负责：

- 运行时调度；
- JSON 序列化实现；
- WPF 视觉树操作；
- ALC 生命周期管理；
- 默认主题应用、默认导航、默认对话框等具体行为。

### Axinyp.Wpf.Hosting

负责：

- 在 Build 前收集模块；
- 进行模块依赖排序、重复校验、缺失依赖检测；
- 启动/停止模块异步生命周期；
- 发现启动期插件并使用 collectible ALC 加载；
- 提供本地 JSON 设置与基于 `ResourceManager` 的本地化实现。

### Axinyp.Wpf.Hosting.Wpf

负责：

- 显式 View 注册与 View 定位；
- Window 管理；
- Region 注册与导航；
- 对话框生命周期；
- 动态主题应用；
- 动态本地化绑定源；
- UI Contribution 聚合目录。

## 关键运行流程

### 1. 模块装配流程

1. 调用方通过 `AddModularHosting()` 初始化宿主核心服务；
2. 通过 `AddModule(...)` 暂存模块；
3. `BuildModularHost()` 时统一执行：
   - 模块重复校验；
   - 依赖排序；
   - `ConfigureServices` 调用；
   - `IModuleCatalog` 冻结写入容器；
4. Host 启动后，由生命周期服务执行：
   - `InitializeAsync`；
   - `IStartupTask.ExecuteAsync`；
   - `StartAsync`；
   - 停止阶段反向执行 `StopAsync`。

### 2. 启动期插件流程

1. 读取每个插件目录中的 `plugin.json`；
2. 校验清单字段、重复插件 ID、缺失依赖、依赖环；
3. 使用 `AssemblyLoadContext` 隔离加载插件程序集；
4. 共享 Abstractions 程序集，确保宿主与插件看到的是同一份公共接口类型；
5. 把发现到的模块回灌到模块装配流程中。

### 3. WPF 表示层流程

1. 调用方显式注册 `ViewModel -> View` 映射；
2. `IWindowManager` / `INavigationService` / `IDialogService` 根据映射创建 View；
3. `ViewCompositionFactory` 统一生成 `View + ViewModel + Scope` 组合；
4. 区域切换、窗口关闭、对话框结束时统一释放 scope；
5. 所有 WPF UI 调度通过可替换的 `IUiDispatcher` 边界，关闭后拒绝新工作；
6. UI Contribution 只返回元数据，最终如何渲染交给具体 Shell 决定；每个 Provider 在独立 scope 中执行，snapshot 不保留 Provider 实例。

## 本地包验证策略

当前仓库只验证“本地可打包、可恢复、可编译”，不做线上发布。

因此当前工程化目标是：

1. `dotnet build` 通过；
2. `dotnet test` 通过；
3. `dotnet pack` 生成可用本地包；
4. 外部消费者通过本地目录源恢复这些包并完成最小宿主编译；
5. 若本地包消费链路成立，再考虑是否需要回接业务项目。

这也是 `consumer-smoke/` 存在的原因：它不是业务项目，也不是样例项目，而是专门验证“离开源码 ProjectReference 之后，包依赖声明是否仍然成立”。

## 当前短板与长期优化方向

下面这些内容不是“现在不能用”，而是“开始真实业务回接后大概率会持续暴露并需要迭代补强”的部分。

### 2. 失败诊断的公共安全边界

框架对“可恢复失败”同时提供两层信息：

- `IPluginCatalog.Failures`、`IWpfUiContributionCatalog.Failures` 和 `NavigationResult` 只包含稳定的失败分类、诊断代码、异常类型名与安全上下文，可由 consumer 用于显示、重试策略或支持请求关联；
- 原始 `Exception` 只写入框架的 `ILogger` 事件，绝不保存在公共 failure 结果、导航结果或冻结 catalog 中。

此边界既避免将异常文本、导航参数或 Provider 返回对象泄露给 UI，也避免公共失败对象强引用可收集插件 ALC 中的异常图。当前稳定事件编号范围为：

- 插件发现：`1040`；
- WPF 导航：`1100–1104`；
- WPF UI Contribution：`1140–1145`。

Consumer 应向用户展示 public result 的安全消息和诊断代码；日志系统则可借由同一代码、EventId 和结构化属性定位根因。导航日志明确不会记录 `NavigationContext.Parameter`。

### 3. Shell 层仍然偏薄

当前基础设施已经具备 Window / Region / Navigation / Dialog / Theme / Localization 的底座，
但还没有形成一个真正标准化的 Shell 组织模型，例如：

- 菜单、工具栏、状态栏的统一宿主结构；
- 导航页、工作区、设置页、浮窗之间的组合规范；
- UI Contribution 最终如何稳定落到 Shell 上的推荐实现；
- 多窗口应用下的主窗口 / 子窗口 / 模态对话框组织约定。

这意味着当前更像“底座 + 扩展点”，还不是“完整应用骨架”。

**长期优化建议：**

1. 增加一个可选 Shell 包或 Shell 样例；
2. 抽象菜单、工具栏、状态栏、设置页容器模型；
3. 为 UI Contribution 提供一套参考渲染实现；
4. 给出单窗口 / 多窗口应用的官方推荐组织方式。

### 2. 可观测性与诊断能力还不够厚

当前已经有较清晰的代码注释和异常路径，但运行时诊断层面仍可增强：

- 模块装配顺序的结构化日志；
- 插件发现、依赖校验、程序集共享命中的日志；
- Region 注册、导航失败、Dialog 关闭失败的诊断输出；
- Theme / Localization 切换的状态变化日志；
- 对外暴露更稳定的异常信息和错误码语义。

**长期优化建议：**

1. 为关键流程定义统一日志事件 ID；
2. 把插件加载、模块排序、导航失败整理为可观察事件；
3. 必要时增加诊断快照或调试辅助 API；
4. 为最常见失败路径补充“面向使用者”的错误提示文案。

### 4. WPF 线程模型已收敛，仍需扩大边界验证

当前 `IUiDispatcher` 已将 Window / Navigation / Dialog / Theme / Localization 的 Dispatcher 访问收敛为可替换、可等待的统一入口。Dispatcher 关闭后，框架拒绝新的 UI 工作；Region 则遵循“可用时脱离视觉树、关闭期仍释放 scope”的双阶段清理规则。

后续仍应继续增加：

- 更复杂 UI 生命周期场景的可控性；
- Dispatcher 的检查后、执行前关闭竞态覆盖；
- 线程模型与资源释放时序图。

### 4. 测试覆盖已经可用，但还不算“框架级充分”

当前 build/test 已经证明主链路可用，但边角路径仍然值得继续压测：

- Dispatcher 正在关闭时的窗口/导航/主题切换行为；
- Dialog 提前关闭、请求关闭与异常关闭路径；
- Region 快速切换与回退边界；
- 插件版本冲突、依赖链更复杂时的行为；
- Theme / Localization 资源缺失时的降级路径；
- 本地包消费者在 Release / clean restore 场景下的持续稳定性。

**长期优化建议：**

1. 继续补 WPF 基础设施边界测试；
2. 增加更复杂插件图测试；
3. 把 `consumer-smoke` 纳入长期验证策略；
4. 逐步建立“回接业务场景 -> 回归测试 -> 修复抽象”的闭环。

### 5. 插件模型当前仍是启动期插件，不是完整运行时扩展系统

这是一个非常重要的边界：

当前插件模型承诺的是：

- 构建前发现；
- 启动期加载；
- 每个插件使用 collectible `AssemblyLoadContext` 做依赖隔离；
- 共享经过身份约束的公共契约；
- 生命周期跟随宿主释放，并在 Host Dispose 时反向清理后调用 `Unload()`。

插件代码被视为受信任代码。`AssemblyLoadContext` 不是安全边界，也不能阻止插件访问宿主进程允许访问的资源。

当前**不承诺**：

- 运行时热插拔或无重启 reload；
- 插件商店、插件停用 / 重新加载管理界面；
- 运行时 UI 动态增删；
- 以完整性元数据或 allowlist 代替沙箱、权限隔离或代码信任决策。

当前入口程序集、assembly resolution、exported type discovery、module activation 和 module lifecycle 的错误仍需要进一步拆分为稳定 failure taxonomy；不能把所有 `GetExportedTypes()` / `Activator.CreateInstance()` 失败都理解为同一类 assembly load 错误。

未来运行时插件系统必须单独完成：

1. 版本化 `plugin.json` schema、依赖版本范围和 host API compatibility；
2. shared assembly 的版本/public key 身份校验与 managed/native resolver tracing；
3. stop work、取消并等待任务、WPF detach、scope/module dispose、引用清理、ALC unload 的状态机；
4. WPF catalog snapshot、refresh rollback 和 navigation/resource/event 引用卫生；
5. 真实 DLL fixture、WeakReference GC、Dispatcher closing 与卸载后 reload 隔离测试。

完整设计和审批边界见 [`docs/plugin-system-design.md`](plugin-system-design.md)。

**长期优化建议：**

1. 先把启动期插件打磨稳定；
2. 先完成设计评审和真实 fixture 测试，再决定是否需要运行时插件系统；
3. 若真的要做，必须单独设计插件状态机、UI 热更新策略和资源回收模型；
4. 不建议在当前阶段直接把启动期插件强行演化为热插拔系统。

### 6. 真实业务回接后很可能还会暴露 API 语义细节问题

这是正常现象，而且几乎不可避免。

目前框架已经通过：

- 源码内 build/test；
- 本地打包；
- 本地消费者恢复编译。

但这还不等价于“已经完全通过真实业务验证”。

真实业务回接后可能继续暴露：

- 某些扩展点名字不够直观；
- 某些默认实现太刚性；
- 某些抽象放在 Abstractions 还是实现层需要再调整；
- 某些启动时机与业务初始化顺序会冲突；
- Shell 与业务模块边界还需要再细分。

**长期优化建议：**

1. 把真实业务回接当作第二轮架构验证；
2. 所有回接中暴露的问题优先记入文档，再决定是否下沉为框架能力；
3. 避免在没有业务证据时继续过度抽象。

## 业务回接前的实施原则

在开始把旧 `WhiteBoard.Framework.Hosting*` 回接到 `Axinyp.Wpf.Hosting*` 之前，建议遵循以下原则：

1. **先做最小回接，不做大爆炸式替换**
   - 先选主窗口启动链路、基础导航、设置或一个对话框进行验证；
   - 不要一开始就把所有业务模块、插件和 Shell 逻辑同时迁过去。

2. **优先验证主链路，不优先追求抽象完美**
   - 重点验证启动、显示窗口、导航、主题、本地化、设置是否顺畅；
   - 抽象缺口先记录，等出现真实阻力再收敛。

3. **每暴露一个短板，都先写入文档再修**
   - 防止修完就忘；
   - 也防止同类问题在后续迁移阶段反复出现。

4. **坚持“一轮回接，一轮回归验证”**
   - 每完成一批迁移，就重新 build/test；
   - 必要时新增针对真实业务问题的回归测试。

## 迭代验证策略

后续建议采用以下固定节奏：

### 第 1 阶段：最小业务回接验证

目标：

- 使用真实业务项目验证主窗口启动；
- 验证一个 Region 导航链路；
- 验证一个设置或主题切换链路；
- 验证一个对话框链路。

输出：

- 记录第一批 API 不顺手点；
- 记录第一批 Shell 缺口；
- 记录第一批生命周期或线程问题。

### 第 2 阶段：框架回收修正

目标：

- 根据真实问题修正抽象；
- 补充对应测试；
- 更新 README / architecture / DESIGN 文档。

输出：

- 第二轮更稳定的基础设施边界。

### 第 3 阶段：扩大业务覆盖面

目标：

- 继续迁移更多模块；
- 验证 UI Contribution、插件、设置分类、复杂导航等场景；
- 累积更完整的使用范式。

输出：

- 真正接近“可长期复用的内部通用框架”。

## 当前约束

- 不承诺运行时热插拔 UI；当前仅保证“构建前发现与加载”的启动期插件模型；
- 不引入额外第三方 IoC 容器；
- 不默认使用命名约定扫描；
- 当前不做任何线上包发布动作，只保留本地包与本地消费者验证能力；
- 所有关键边界代码必须带清晰中文注释，优先解释“为什么这样设计”和“调用时机/限制条件”。

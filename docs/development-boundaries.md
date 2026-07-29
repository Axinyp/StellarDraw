# 开发边界与冻结策略

## 目的

`Axinyp.Wpf.Hosting` 是后续唯一演进的基础设施主线。白板是第一个真实消费者，用来验证框架的宿主、模块、WPF 表示层、生命周期和包边界；白板领域能力不是框架能力。

本策略防止在同一历史仓库和共享 worktree 中，将框架演进、旧白板功能迭代和临时迁移代码混入同一个提交。

## 可继续演进的路径

以下路径可以承载 Axinyp 框架、样例、消费者验证、测试、文档和工程化改动：

```text
Axinyp.Wpf.Hosting.slnx
Directory.Build.props
Directory.Packages.props
global.json
.github/workflows/
docs/
samples/
consumer-smoke/
consumers/
src/Axinyp.Wpf.Hosting.Abstractions/
src/Axinyp.Wpf.Hosting/
src/Axinyp.Wpf.Hosting.Wpf/
tests/Axinyp.Wpf.Hosting.Tests/
tests/Axinyp.Wpf.Hosting.Wpf.Tests/
tests/StellarDraw.Axinyp.Spike.Tests/
```

框架提交必须只包含上述路径，或包含为框架构建、打包与验证所必需的顶层元数据。提交前必须逐文件审查暂存区；禁止使用 `git add .`。

## 冻结的白板基线

以下路径仍可被读取、构建和作为兼容性/行为验证参考，但不再承载新功能、框架演进或迁移临时补丁：

```text
src/WhiteBoard.AI/
src/WhiteBoard.Client/
src/WhiteBoard.Contracts/
src/WhiteBoard.Contracts.Wpf/
src/WhiteBoard.Core/
src/WhiteBoard.Framework.Hosting/
src/WhiteBoard.Framework.Hosting.Wpf/
src/WhiteBoard.Host.Demo/
src/WhiteBoard.Mcp.Core/
src/WhiteBoard.McpHost/
src/WhiteBoard.Plugin/
src/WhiteBoard.Protocol/
src/WhiteBoard.UI/
tests/WhiteBoard.*/
WhiteBoard.slnx
```

例外仅限于：

1. 阻断框架验证的构建修复；
2. 明确的安全修复；
3. 为观察冻结行为而增加的测试或诊断。

任何例外都必须单独记录决策、单独提交，并说明为什么不能在 Axinyp 或 consumer 层解决。

## 白板作为消费者的边界

- 新的白板接入必须位于 `consumers/` 或未来独立产品仓库，不能回写冻结的 `src/WhiteBoard.*`。
- Axinyp 只依赖自己的 `Abstractions`、`Hosting`、`Hosting.Wpf` 分层；不得引用任何 `WhiteBoard.*` 项目。
- 旧 `IWhiteboardWpfPlugin` 与 Axinyp 的 `IModule` 是不同模型：前者是运行时 WPF view-plugin，后者是 Build 前 DI 装配单元。不得通过改名、继承或双重注册将两者混为一谈。
- 如需展示冻结白板 UI，只能在 consumer 私有 bridge 中使用其公开插件契约。bridge 不得访问插件私有字段、不得导出 `WhiteboardSession`、不得成为 Axinyp 公共 API。
- Axinyp 的启动期插件只承诺 manifest 发现、Build 前模块装配与随 Host 停止释放；不承诺运行时热插拔，也不是不可信代码的安全沙箱。

## 提交与验证规则

每个框架变更至少应完成：

1. `Axinyp.Wpf.Hosting.slnx` 的独立 restore、build、test；
2. Release pack 后由 `consumer-smoke` 在本地包源 clean restore/build；
3. 对公开 API、插件加载或 WPF 生命周期改动补充框架级测试与文档；
4. `StellarDraw.Axinyp.Spike.Tests` 的默认回归只验证 consumer-private bridge、参数边界和 Shell 激活；标记为 `LegacyCompatibility` 的测试必须由 CI 单独构建受信 legacy artifact 后运行，并通过 `STELLARDRAW_TRUSTED_LEGACY_PLUGIN` 注入绝对 DLL 路径。未提供该变量时，该 smoke 不加载任何 artifact；它不能让 Axinyp solution 或公共包依赖冻结插件；
5. 新 consumer 仅使用 `PackageReference`，不能通过源码 `ProjectReference` 掩盖包边界问题；
6. 对大于 30 行的改动执行变更、质量与安全检查。

在独立远端、维护权限、分支保护和发布治理明确前，不进行任何线上包发布。

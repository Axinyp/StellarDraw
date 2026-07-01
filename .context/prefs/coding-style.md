# Coding Style Guide

> 此文件定义团队编码规范，所有 LLM 工具在修改代码时必须遵守。
> 提交到 Git，团队共享。

## General
- Prefer small, reviewable changes; avoid unrelated refactors.
- Keep functions short (<50 lines); avoid deep nesting (≤3 levels).
- Name things explicitly; no single-letter variables except loop counters.
- Handle errors explicitly; never swallow errors silently.

## Language-Specific

### C# / .NET 10 (WPF)
- 所有 Session 操作通过 `PostOperationAsync` 在执行器线程运行，禁止从 UI 线程直接修改领域对象。
- 几何变更必须通过 `SpatialIndex.Update(obj, mutate)` 原子更新，保持索引同步。
- Transform 矩阵采用行向量约定（x'=x·M11+y·M21+OffsetX），旋转角顺时针为正。
- 新增命令类实现 `IWhiteboardCommand`，通过 `_history.Execute(cmd)` 注册到撤销栈。
- 优先使用 `async/await`，避免阻塞 UI 线程；不使用 `.Result` / `.Wait()`。

## Git Commits
- Conventional Commits, imperative mood.
- Atomic commits: one logical change per commit.

## Testing
- Every feat/fix MUST include corresponding tests.
- Coverage must not decrease.
- Fix flow: write failing test FIRST, then fix code.

## Security
- Never log secrets (tokens/keys/cookies/JWT).
- Validate inputs at trust boundaries.
- 禁止硬编码 API Key / LLM 凭据；通过配置或环境变量注入。

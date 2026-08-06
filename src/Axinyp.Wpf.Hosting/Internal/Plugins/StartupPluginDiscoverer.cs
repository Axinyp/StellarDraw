using System.Reflection;
using System.Text.Json;
using Axinyp.Wpf.Hosting.Abstractions.Modules;
using Axinyp.Wpf.Hosting.Abstractions.Plugins;
using Axinyp.Wpf.Hosting.Plugins;

namespace Axinyp.Wpf.Hosting.Internal.Plugins;

/// <summary>启动期插件发现器：先验证清单和依赖，再隔离加载每个插件程序集。</summary>
internal static class StartupPluginDiscoverer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static (PluginCatalog Catalog, StartupPluginRuntime Runtime, IReadOnlyList<StartupPluginDiagnostic> Diagnostics)
        Discover(PluginDiscoveryOptions options)
    {
        var diagnostics = new List<StartupPluginDiagnostic>();
        var candidates = ReadCandidates(options.PluginDirectories, diagnostics);
        var failures = new List<PluginLoadFailure>();
        var valid = candidates.Where(candidate => candidate.Manifest is not null).ToList();
        failures.AddRange(candidates.Where(candidate => candidate.Failure is not null).Select(candidate => candidate.Failure!));

        RemoveDuplicateIds(valid, failures);
        var ordered = SortValidCandidates(valid, failures);
        var handles = new List<StartupPluginLoadHandle>();

        foreach (var candidate in ordered)
        {
            var failedDependency = candidate.Manifest!.Dependencies.FirstOrDefault(dependency =>
                failures.Any(failure => string.Equals(failure.Id, dependency, StringComparison.OrdinalIgnoreCase)));
            if (failedDependency is not null)
            {
                failures.Add(CreateFailure(candidate.Directory, candidate.Manifest.Id,
                    PluginLoadFailureKind.DependencyFailed, "插件的依赖项未能成功加载。",
                    entryAssembly: candidate.Manifest.EntryAssembly, dependencyId: failedDependency));
                continue;
            }

            try { handles.Add(Load(candidate, options.AdditionalSharedAssemblies)); }
            catch (Exception exception)
            {
                var (kind, message) = exception switch
                {
                    PluginEntryAssemblyMissingException => (PluginLoadFailureKind.EntryAssemblyMissing, "插件入口程序集无法加载。"),
                    PluginModuleDiscoveryException => (PluginLoadFailureKind.ModuleDiscoveryFailed, "无法在入口程序集中发现 IModule 实现。"),
                    PluginModuleActivationException => (PluginLoadFailureKind.ModuleActivationFailed, "插件模块实例化失败。"),
                    _ => (PluginLoadFailureKind.AssemblyLoadFailed, "插件入口程序集无法加载。"),
                };
                var failure = CreateFailure(candidate.Directory, candidate.Manifest.Id, kind,
                    message, candidate.Manifest.EntryAssembly, exception: exception);
                failures.Add(failure);
                diagnostics.Add(new StartupPluginDiagnostic(failure, exception));
            }
        }

        var descriptors = handles.Select(handle => handle.Descriptor).ToList();
        return (
            new PluginCatalog(descriptors, failures),
            new StartupPluginRuntime(handles),
            diagnostics);
    }

    private static List<Candidate> ReadCandidates(
        IEnumerable<string> roots,
        ICollection<StartupPluginDiagnostic> diagnostics)
    {
        var result = new List<Candidate>();
        foreach (var root in roots.Where(Directory.Exists).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var directory in Directory.EnumerateDirectories(root).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                result.Add(ReadCandidate(directory, diagnostics));
        }
        return result;
    }

    private static Candidate ReadCandidate(string directory, ICollection<StartupPluginDiagnostic> diagnostics)
    {
        var manifestPath = Path.Combine(directory, "plugin.json");
        if (!File.Exists(manifestPath))
            return Candidate.Fail(directory, PluginLoadFailureKind.ManifestMissing, "未找到 plugin.json。");

        try
        {
            var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), JsonOptions);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version)
                || string.IsNullOrWhiteSpace(manifest.EntryAssembly))
                return Candidate.Fail(directory, PluginLoadFailureKind.ManifestInvalid, "plugin.json 缺少 Id、Version 或 EntryAssembly。");
            if (Path.IsPathRooted(manifest.EntryAssembly) || manifest.EntryAssembly.Contains("..")
                || !string.Equals(Path.GetExtension(manifest.EntryAssembly), ".dll", StringComparison.OrdinalIgnoreCase))
                return Candidate.Fail(directory, PluginLoadFailureKind.ManifestInvalid, "EntryAssembly 必须是插件目录内的 .dll 文件。", manifest.Id, manifest.EntryAssembly);

            var assemblyPath = Path.GetFullPath(Path.Combine(directory, manifest.EntryAssembly));
            var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
            if (!assemblyPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return Candidate.Fail(directory, PluginLoadFailureKind.ManifestInvalid, "EntryAssembly 越出了插件目录。", manifest.Id, manifest.EntryAssembly);
            if (manifest.Dependencies.Any(string.IsNullOrWhiteSpace) || manifest.Dependencies.Contains(manifest.Id, StringComparer.OrdinalIgnoreCase))
                return Candidate.Fail(directory, PluginLoadFailureKind.ManifestInvalid, "Dependencies 包含空值或自依赖。", manifest.Id, manifest.EntryAssembly);

            return new Candidate(directory, assemblyPath, manifest, null);
        }
        catch (JsonException exception)
        {
            var failure = CreateFailure(directory, null, PluginLoadFailureKind.ManifestInvalid,
                "plugin.json 格式无效。", exception: exception);
            diagnostics.Add(new StartupPluginDiagnostic(failure, exception));
            return new Candidate(directory, "", null, failure);
        }
    }

    private static void RemoveDuplicateIds(List<Candidate> candidates, List<PluginLoadFailure> failures)
    {
        // 同一物理目录被重叠的根目录重复扫到时只保留第一份：这是配置冗余而非 ID 冲突。
        var seenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        candidates.RemoveAll(candidate => !seenDirectories.Add(Path.GetFullPath(candidate.Directory)));

        // 真正不同目录声明同一 ID 属于无法自动仲裁的冲突：整组拒绝并逐一记录，避免静默偏袒任一方。
        foreach (var group in candidates.GroupBy(c => c.Manifest!.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            foreach (var candidate in group)
                failures.Add(CreateFailure(candidate.Directory, candidate.Manifest!.Id,
                    PluginLoadFailureKind.DuplicatePluginId, "插件 Id 重复。", candidate.Manifest.EntryAssembly));
            candidates.RemoveAll(candidate => group.Contains(candidate));
        }
    }

    private static IReadOnlyList<Candidate> SortValidCandidates(List<Candidate> candidates, List<PluginLoadFailure> failures)
    {
        var byId = candidates.ToDictionary(candidate => candidate.Manifest!.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates.ToList())
        {
            var missing = candidate.Manifest!.Dependencies.FirstOrDefault(dependency => !byId.ContainsKey(dependency));
            if (missing is null) continue;
            failures.Add(CreateFailure(candidate.Directory, candidate.Manifest.Id,
                PluginLoadFailureKind.MissingDependency, $"缺少插件依赖 '{missing}'。",
                candidate.Manifest.EntryAssembly, missing));
            candidates.Remove(candidate);
        }

        var result = new List<Candidate>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates.ToList()) Visit(candidate);
        return result;

        void Visit(Candidate candidate)
        {
            var id = candidate.Manifest!.Id;
            if (done.Contains(id)) return;
            if (!visiting.Add(id))
            {
                failures.Add(CreateFailure(candidate.Directory, id, PluginLoadFailureKind.DependencyCycle,
                    "检测到插件依赖循环。", candidate.Manifest.EntryAssembly));
                return;
            }

            foreach (var dependency in candidate.Manifest.Dependencies)
                if (byId.TryGetValue(dependency, out var dependencyCandidate)) Visit(dependencyCandidate);
            visiting.Remove(id);
            done.Add(id);
            if (!failures.Any(failure => string.Equals(failure.Id, id, StringComparison.OrdinalIgnoreCase))) result.Add(candidate);
        }
    }

    private static StartupPluginLoadHandle Load(Candidate candidate, IEnumerable<Assembly> shared)
    {
        if (!File.Exists(candidate.AssemblyPath))
            throw new PluginEntryAssemblyMissingException("找不到入口程序集。");

        var context = new StartupPluginLoadContext(candidate.AssemblyPath, shared);
        try
        {
            var assembly = context.LoadFromAssemblyPath(candidate.AssemblyPath);

            Type[] exportedTypes;
            try
            {
                exportedTypes = assembly.GetExportedTypes();
            }
            catch (Exception exception)
            {
                throw new PluginModuleDiscoveryException("无法枚举入口程序集的导出类型。", exception);
            }

            var moduleTypes = exportedTypes
                .Where(type => !type.IsAbstract && typeof(IModule).IsAssignableFrom(type))
                .ToList();
            if (moduleTypes.Count == 0)
                throw new PluginModuleDiscoveryException("入口程序集没有公开 IModule 实现。");

            var modules = new List<IModule>(moduleTypes.Count);
            foreach (var type in moduleTypes)
            {
                try
                {
                    modules.Add((IModule)(Activator.CreateInstance(type)
                        ?? throw new InvalidOperationException($"无法创建模块 {type.FullName}。")));
                }
                catch (Exception exception) when (exception is not PluginModuleActivationException)
                {
                    throw new PluginModuleActivationException($"无法创建模块 {type.FullName}。", exception);
                }
            }

            var descriptor = new PluginDescriptor(candidate.Manifest!.Id, candidate.Manifest.Version, candidate.Directory,
                candidate.Manifest.EntryAssembly, candidate.Manifest.Dependencies, modules.Select(module => module.GetType().FullName ?? module.GetType().Name).ToList());
            return new StartupPluginLoadHandle(descriptor, context, modules);
        }
        catch { context.Unload(); throw; }
    }

    private static PluginLoadFailure CreateFailure(
        string directory,
        string? id,
        PluginLoadFailureKind kind,
        string message,
        string? entryAssembly = null,
        string? dependencyId = null,
        Exception? exception = null)
        => new PluginLoadFailure(directory, id, kind, message)
        {
            Metadata = new PluginLoadFailureMetadata(
                $"plugin.discovery.{kind}",
                PluginDiscoveryEventIds.DiscoveryFailure.Id,
                entryAssembly,
                dependencyId,
                exception?.GetType().FullName)
        };

    private sealed class PluginEntryAssemblyMissingException(string message) : InvalidOperationException(message);

    private sealed class PluginModuleDiscoveryException(string message, Exception? inner = null)
        : InvalidOperationException(message, inner);

    private sealed class PluginModuleActivationException(string message, Exception inner)
        : InvalidOperationException(message, inner);

    private sealed record Candidate(string Directory, string AssemblyPath, PluginManifest? Manifest, PluginLoadFailure? Failure)
    {
        public static Candidate Fail(
            string directory,
            PluginLoadFailureKind kind,
            string message,
            string? id = null,
            string? entryAssembly = null)
            => new(directory, "", null, CreateFailure(directory, id, kind, message, entryAssembly));
    }
}

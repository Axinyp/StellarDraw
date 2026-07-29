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

    public static (PluginCatalog Catalog, StartupPluginRuntime Runtime) Discover(PluginDiscoveryOptions options)
    {
        var candidates = ReadCandidates(options.PluginDirectories);
        var failures = new List<PluginLoadFailure>();
        var valid = candidates.Where(candidate => candidate.Manifest is not null).ToList();
        failures.AddRange(candidates.Where(candidate => candidate.Failure is not null).Select(candidate => candidate.Failure!));

        RemoveDuplicateIds(valid, failures);
        var ordered = SortValidCandidates(valid, failures);
        var handles = new List<StartupPluginLoadHandle>();

        foreach (var candidate in ordered)
        {
            if (candidate.Manifest!.Dependencies.Any(dependency =>
                failures.Any(failure => string.Equals(failure.Id, dependency, StringComparison.OrdinalIgnoreCase))))
            {
                failures.Add(new PluginLoadFailure(candidate.Directory, candidate.Manifest.Id,
                    PluginLoadFailureKind.DependencyFailed, "插件的依赖项未能成功加载。"));
                continue;
            }

            try { handles.Add(Load(candidate, options.AdditionalSharedAssemblies)); }
            catch (Exception ex)
            {
                var kind = ex is PluginEntryAssemblyMissingException
                    ? PluginLoadFailureKind.EntryAssemblyMissing
                    : PluginLoadFailureKind.AssemblyLoadFailed;
                failures.Add(new PluginLoadFailure(candidate.Directory, candidate.Manifest.Id, kind, ex.Message));
            }
        }

        var descriptors = handles.Select(handle => handle.Descriptor).ToList();
        return (new PluginCatalog(descriptors, failures), new StartupPluginRuntime(handles));
    }

    private static List<Candidate> ReadCandidates(IEnumerable<string> roots)
    {
        var result = new List<Candidate>();
        foreach (var root in roots.Where(Directory.Exists).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var directory in Directory.EnumerateDirectories(root).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                result.Add(ReadCandidate(directory));
        }
        return result;
    }

    private static Candidate ReadCandidate(string directory)
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
                return Candidate.Fail(directory, PluginLoadFailureKind.ManifestInvalid, "EntryAssembly 必须是插件目录内的 .dll 文件。", manifest.Id);

            var assemblyPath = Path.GetFullPath(Path.Combine(directory, manifest.EntryAssembly));
            var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
            if (!assemblyPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return Candidate.Fail(directory, PluginLoadFailureKind.ManifestInvalid, "EntryAssembly 越出了插件目录。", manifest.Id);
            if (manifest.Dependencies.Any(string.IsNullOrWhiteSpace) || manifest.Dependencies.Contains(manifest.Id, StringComparer.OrdinalIgnoreCase))
                return Candidate.Fail(directory, PluginLoadFailureKind.ManifestInvalid, "Dependencies 包含空值或自依赖。", manifest.Id);

            return new Candidate(directory, assemblyPath, manifest, null);
        }
        catch (JsonException ex) { return Candidate.Fail(directory, PluginLoadFailureKind.ManifestInvalid, ex.Message); }
    }

    private static void RemoveDuplicateIds(List<Candidate> candidates, List<PluginLoadFailure> failures)
    {
        foreach (var group in candidates.GroupBy(c => c.Manifest!.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            foreach (var candidate in group)
                failures.Add(new PluginLoadFailure(candidate.Directory, candidate.Manifest!.Id, PluginLoadFailureKind.DuplicatePluginId, "插件 Id 重复。"));
            candidates.RemoveAll(c => group.Contains(c));
        }
    }

    private static IReadOnlyList<Candidate> SortValidCandidates(List<Candidate> candidates, List<PluginLoadFailure> failures)
    {
        var byId = candidates.ToDictionary(c => c.Manifest!.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates.ToList())
        {
            var missing = candidate.Manifest!.Dependencies.FirstOrDefault(dependency => !byId.ContainsKey(dependency));
            if (missing is null) continue;
            failures.Add(new PluginLoadFailure(candidate.Directory, candidate.Manifest.Id, PluginLoadFailureKind.MissingDependency, $"缺少插件依赖 '{missing}'。"));
            candidates.Remove(candidate);
        }
        // 简洁的稳定 DFS；循环候选记录失败并剔除，非相关插件照常加载。
        var result = new List<Candidate>(); var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates.ToList()) Visit(candidate);
        return result;
        void Visit(Candidate candidate)
        {
            var id = candidate.Manifest!.Id;
            if (done.Contains(id)) return;
            if (!visiting.Add(id)) { failures.Add(new PluginLoadFailure(candidate.Directory, id, PluginLoadFailureKind.DependencyCycle, "检测到插件依赖循环。")); return; }
            foreach (var dependency in candidate.Manifest.Dependencies)
                if (byId.TryGetValue(dependency, out var dependencyCandidate)) Visit(dependencyCandidate);
            visiting.Remove(id); done.Add(id);
            if (!failures.Any(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase))) result.Add(candidate);
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
            var modules = assembly.GetExportedTypes().Where(type => !type.IsAbstract && typeof(IModule).IsAssignableFrom(type))
                .Select(type => (IModule)(Activator.CreateInstance(type) ?? throw new InvalidOperationException($"无法创建模块 {type.FullName}。"))).ToList();
            if (modules.Count == 0) throw new InvalidOperationException("入口程序集没有公开 IModule 实现。");
            var descriptor = new PluginDescriptor(candidate.Manifest!.Id, candidate.Manifest.Version, candidate.Directory,
                candidate.Manifest.EntryAssembly, candidate.Manifest.Dependencies, modules.Select(m => m.GetType().FullName ?? m.GetType().Name).ToList());
            return new StartupPluginLoadHandle(descriptor, context, modules);
        }
        catch { context.Unload(); throw; }
    }

    private sealed class PluginEntryAssemblyMissingException(string message) : InvalidOperationException(message);

    private sealed record Candidate(string Directory, string AssemblyPath, PluginManifest? Manifest, PluginLoadFailure? Failure)
    {
        public static Candidate Fail(string directory, PluginLoadFailureKind kind, string message, string? id = null)
            => new(directory, "", null, new PluginLoadFailure(directory, id, kind, message));
    }
}

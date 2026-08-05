namespace StellarDraw.Axinyp.Spike;

/// <summary>
/// Spike 启动时可选的 legacy plugin 输入。
/// </summary>
internal sealed record LegacyPluginOptions(string? AssemblyPath)
{
    /// <summary>
    /// 从应用命令行解析 <c>--legacy-plugin &lt;absolute-dll-path&gt;</c>。
    /// 未提供参数时 Shell 仍会启动，但不会加载 legacy plugin。
    /// </summary>
    public static LegacyPluginOptions FromArguments(IReadOnlyList<string> arguments)
    {
        const string optionName = "--legacy-plugin";
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], optionName, StringComparison.Ordinal))
                continue;

            if (index + 1 >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index + 1]))
                throw new ArgumentException($"{optionName} 必须提供 legacy plugin DLL 的绝对路径。", optionName);

            return new LegacyPluginOptions(arguments[index + 1]);
        }

        return new LegacyPluginOptions((string?)null);
    }
}

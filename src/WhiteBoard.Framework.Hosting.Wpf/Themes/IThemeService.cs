using System.Windows;

namespace WhiteBoard.Framework.Hosting.Wpf.Themes;

public sealed record ThemeDescriptor(string Id, string DisplayName, Uri ResourceUri, bool IsDefault = false);

public interface IThemeService
{
    ThemeDescriptor CurrentTheme { get; }
    IReadOnlyList<ThemeDescriptor> Themes { get; }
    event EventHandler? ThemeChanged;
    ValueTask ApplyAsync(string themeId, CancellationToken cancellationToken = default);
}

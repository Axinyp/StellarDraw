namespace WhiteBoard.Framework.Hosting.Wpf.Themes;

public sealed class ThemeOptions
{
    public string SettingsKey { get; set; } = "ui.theme";
    public IList<ThemeDescriptor> Themes { get; } = [];
}

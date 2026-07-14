using System.Globalization;
using System.Resources;

namespace WhiteBoard.Framework.Hosting.Localization;

public sealed class LocalizationOptions
{
    public string SettingsKey { get; set; } = "ui.culture";
    public CultureInfo DefaultCulture { get; set; } = CultureInfo.GetCultureInfo("zh-CN");
    public IList<CultureInfo> SupportedCultures { get; } = [];
    public IList<ResourceManager> ResourceManagers { get; } = [];
}

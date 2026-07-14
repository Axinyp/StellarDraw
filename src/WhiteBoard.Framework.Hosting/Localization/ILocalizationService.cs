using System.Globalization;

namespace WhiteBoard.Framework.Hosting.Localization;

public interface ILocalizationService
{
    CultureInfo CurrentCulture { get; }
    event EventHandler? CultureChanged;
    string GetString(string key);
    ValueTask SetCultureAsync(CultureInfo culture, CancellationToken cancellationToken = default);
}

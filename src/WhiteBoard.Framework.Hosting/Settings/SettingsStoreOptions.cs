namespace WhiteBoard.Framework.Hosting.Settings;

/// <summary>本地 JSON 偏好存储选项。</summary>
public sealed class SettingsStoreOptions
{
    public string ApplicationName { get; set; } = "StellarDraw";
    public string? DirectoryPath { get; set; }
    public string FileName { get; set; } = "settings.json";

    internal string GetFilePath() => Path.Combine(
        DirectoryPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationName),
        FileName);
}

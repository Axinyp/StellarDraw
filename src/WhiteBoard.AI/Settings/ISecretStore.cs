namespace WhiteBoard.AI.Settings;

/// <summary>API Key 等敏感信息的读写抽象，默认实现使用 Windows Credential Manager (DPAPI)。</summary>
public interface ISecretStore
{
    void Save(string key, string secret);

    string Load(string key);

    void Delete(string key);

    bool Exists(string key);
}
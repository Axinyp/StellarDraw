using System.Runtime.InteropServices;
using System.Text;

namespace WhiteBoard.AI.Settings;

/// <summary>
/// 基于 Windows Credential Manager (DPAPI) 的密钥存储。
/// 所有 API Key 通过 CredWrite/CredRead 存储在当前用户凭据管理器中，
/// 由操作系统 DPAPI 加密，进程间不可读取。
/// 严禁：明文日志、settings.json、IPluginMessageBus 传递。
/// </summary>
public sealed class WindowsCredentialStore : ISecretStore
{
    private const string Prefix = "WhiteBoardAI_";

    public void Save(string key, string secret)
    {
        var target = Prefix + key;
        var bytes  = Encoding.Unicode.GetBytes(secret);

        var cred = new CREDENTIAL
        {
            Type               = CRED_TYPE_GENERIC,
            TargetName         = target,
            CredentialBlobSize = (uint)bytes.Length,
            CredentialBlob     = Marshal.AllocHGlobal(bytes.Length),
            Persist            = CRED_PERSIST_LOCAL_MACHINE,
            UserName           = Environment.UserName,
        };

        try
        {
            Marshal.Copy(bytes, 0, cred.CredentialBlob, bytes.Length);
            if (!CredWrite(ref cred, 0))
                throw new InvalidOperationException(
                    $"CredWrite failed: {Marshal.GetLastWin32Error()}");
        }
        finally
        {
            Marshal.FreeHGlobal(cred.CredentialBlob);
        }
    }

    public string Load(string key)
    {
        var target = Prefix + key;
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var credPtr))
            return string.Empty;

        try
        {
            var cred  = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public void Delete(string key)
    {
        CredDelete(Prefix + key, CRED_TYPE_GENERIC, 0);
    }

    public bool Exists(string key)
    {
        if (!CredRead(Prefix + key, CRED_TYPE_GENERIC, 0, out var ptr)) return false;
        CredFree(ptr);
        return true;
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────────

    private const uint CRED_TYPE_GENERIC        = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint    Flags;
        public uint    Type;
        public string? TargetName;
        public string? Comment;
        public long    LastWritten;
        public uint    CredentialBlobSize;
        public IntPtr  CredentialBlob;
        public uint    Persist;
        public uint    AttributeCount;
        public IntPtr  Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL userCredential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags,
        out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}

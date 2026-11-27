using System.Runtime.InteropServices;
using System.Text;

namespace XantaComfyLauncher.Utils;

public static class GitHubTokenStore
{
    private const string TargetName = "XantaComfyLauncher/GitHubToken";
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public static string? GetStoredToken()
    {
        if (!OperatingSystem.IsWindows()) return null;
        IntPtr credentialPtr = IntPtr.Zero;
        try
        {
            if (!CredRead(TargetName, CredTypeGeneric, 0, out credentialPtr)) return null;
            var credential = Marshal.PtrToStructure<Credential>(credentialPtr);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero) return null;
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        catch
        {
            return null;
        }
        finally
        {
            if (credentialPtr != IntPtr.Zero) CredFree(credentialPtr);
        }
    }

    public static string? GetEffectiveToken()
    {
        var stored = GetStoredToken();
        if (!string.IsNullOrWhiteSpace(stored)) return stored;
        var specific = Environment.GetEnvironmentVariable("XANTA_LAUNCHER_GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(specific)) return specific.Trim();
        var generic = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        return string.IsNullOrWhiteSpace(generic) ? null : generic.Trim();
    }

    public static string GetTokenSource()
    {
        if (!string.IsNullOrWhiteSpace(GetStoredToken())) return "Windows 凭据管理器";
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("XANTA_LAUNCHER_GITHUB_TOKEN")))
            return "环境变量 XANTA_LAUNCHER_GITHUB_TOKEN";
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_TOKEN")))
            return "环境变量 GITHUB_TOKEN";
        return "未配置";
    }

    public static void SaveToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token 不能为空。", nameof(token));
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Token 安全存储仅支持 Windows。");

        var bytes = Encoding.Unicode.GetBytes(token.Trim());
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = TargetName,
                Comment = "GitHub token for XantaComfyLauncher node search",
                CredentialBlob = blob,
                CredentialBlobSize = (uint)bytes.Length,
                Persist = CredPersistLocalMachine,
                UserName = "XantaComfyLauncher"
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException("写入 Windows 凭据管理器失败：" + Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    public static void ClearToken()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (CredDelete(TargetName, CredTypeGeneric, 0)) return;
        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
            throw new InvalidOperationException("删除 Windows 凭据失败：" + error);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}

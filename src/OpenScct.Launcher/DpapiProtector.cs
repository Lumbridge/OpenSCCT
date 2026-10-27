using System.Security.Cryptography;
using System.Text;
using OpenScct.Launcher.Settings;

namespace OpenScct.Launcher.App;

/// <summary>Windows DPAPI for the current user: only this Windows account on this machine can decrypt.</summary>
internal sealed class DpapiProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OpenSCCT.Launcher.GitHubToken.v1");

    public byte[] Protect(byte[] plain) => ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedBytes) => ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
}

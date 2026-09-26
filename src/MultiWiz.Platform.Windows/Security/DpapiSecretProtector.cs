using System.Security.Cryptography;
using MultiWiz.Core.Security;

namespace MultiWiz.Platform.Windows.Security;

/// <summary>Windows DPAPI for the current user without extra entropy, matching how MultiWiz 3 protected its config.</summary>
internal sealed class DpapiSecretProtector : ISecretProtector
{
    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        ArgumentNullException.ThrowIfNull(protectedData);
        return ProtectedData.Unprotect(protectedData, null, DataProtectionScope.CurrentUser);
    }
}

using System.Security.Cryptography;
using System.Text;

namespace GPTDeskTop.Services;

public static class GitHubTokenProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GPTDeskTop.GitHubIntegration.v1");

    public static string Protect(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;
        var plain = Encoding.UTF8.GetBytes(token.Trim());
        var cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cipher);
    }

    public static string Unprotect(string protectedToken)
    {
        if (string.IsNullOrWhiteSpace(protectedToken)) return string.Empty;
        var cipher = Convert.FromBase64String(protectedToken);
        var plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }
}

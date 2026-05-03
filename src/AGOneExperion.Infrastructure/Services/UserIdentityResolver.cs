using System.Security.Cryptography;
using System.Text;
using AGOneExperion.Core.Interfaces;

namespace AGOneExperion.Infrastructure.Services;

/// <summary>
/// Resolves a stable user identity.
/// Authenticated users: pass through as-is.
/// Anonymous users: SHA-256( IP + UserAgent ) → hex prefix (first 16 chars) prefixed with "anon-".
/// </summary>
public class UserIdentityResolver : IUserIdentityResolver
{
    public string Resolve(string? claimedUserId, string? ipAddress, string? userAgent, out bool isAnonymous)
    {
        if (!string.IsNullOrWhiteSpace(claimedUserId))
        {
            isAnonymous = false;
            return claimedUserId.Trim();
        }

        isAnonymous = true;
        var raw = $"{ipAddress ?? "unknown"}|{userAgent ?? "unknown"}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return "anon-" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}

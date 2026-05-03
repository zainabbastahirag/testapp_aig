namespace AGOneExperion.Core.Interfaces;

/// <summary>
/// Resolves or generates a stable anonymous user ID from IP and user-agent.
/// When a real authenticated user ID is provided by the SDK, it is passed through.
/// </summary>
public interface IUserIdentityResolver
{
    /// <summary>
    /// Returns a stable user identifier.
    /// If <paramref name="claimedUserId"/> is non-empty it is returned directly.
    /// Otherwise a pseudonymous ID is derived from the IP and user-agent via hashing.
    /// </summary>
    string Resolve(string? claimedUserId, string? ipAddress, string? userAgent, out bool isAnonymous);
}

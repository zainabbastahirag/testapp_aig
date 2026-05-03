using ExperionAgent.Core.Models;

namespace ExperionAgent.Core.Interfaces;

public interface IIdentityResolver
{
    /// <summary>
    /// Resolves a stable user identifier from the available signals.
    /// Uses explicit userId if present, otherwise falls back to
    /// client fingerprint + IP-based geo lookup.
    /// </summary>
    Task<ResolvedIdentity> ResolveAsync(
        string? userId, string? clientFingerprint, string? ipAddress,
        CancellationToken ct = default);
}

public class ResolvedIdentity
{
    public string UserId { get; set; } = string.Empty;
    public bool IsAnonymous { get; set; }
    public GeoLocation? Location { get; set; }
}

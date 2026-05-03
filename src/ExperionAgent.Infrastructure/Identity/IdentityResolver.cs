using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExperionAgent.Core.Interfaces;
using ExperionAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace ExperionAgent.Infrastructure.Identity;

public class IdentityResolver : IIdentityResolver
{
    private readonly ILogger<IdentityResolver> _log;
    private static readonly HttpClient _http = new();

    public IdentityResolver(ILogger<IdentityResolver> log)
    {
        _log = log;
    }

    public async Task<ResolvedIdentity> ResolveAsync(
        string? userId, string? clientFingerprint, string? ipAddress,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(userId))
        {
            _log.LogDebug("[Identity] Authenticated user: {UserId}", userId);
            return new ResolvedIdentity
            {
                UserId = userId,
                IsAnonymous = false,
                Location = await TryGeoLookupAsync(ipAddress, ct)
            };
        }

        var location = await TryGeoLookupAsync(ipAddress, ct);
        var regionTag = location?.Country ?? "unknown";

        var raw = $"{clientFingerprint ?? "none"}|{ipAddress ?? "0.0.0.0"}";
        var hash = ComputeShortHash(raw);
        var syntheticId = $"anon_{hash}_{regionTag.ToLowerInvariant()}";

        _log.LogDebug("[Identity] Anonymous user resolved: {SyntheticId}", syntheticId);

        return new ResolvedIdentity
        {
            UserId = syntheticId,
            IsAnonymous = true,
            Location = location
        };
    }

    private async Task<GeoLocation?> TryGeoLookupAsync(string? ip, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ip) || ip == "::1" || ip.StartsWith("127."))
            return new GeoLocation { Country = "local", Region = "local", City = "localhost" };

        try
        {
            var response = await _http.GetStringAsync($"http://ip-api.com/json/{ip}?fields=country,regionName,city,timezone", ct);
            var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            return new GeoLocation
            {
                Country = root.TryGetProperty("country", out var c) ? c.GetString() : null,
                Region = root.TryGetProperty("regionName", out var r) ? r.GetString() : null,
                City = root.TryGetProperty("city", out var ci) ? ci.GetString() : null,
                Timezone = root.TryGetProperty("timezone", out var tz) ? tz.GetString() : null
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[Identity] Geo lookup failed for IP {Ip}", ip);
            return null;
        }
    }

    private static string ComputeShortHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }
}

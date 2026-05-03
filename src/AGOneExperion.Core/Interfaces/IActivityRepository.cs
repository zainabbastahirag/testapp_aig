using AGOneExperion.Core.Models;

namespace AGOneExperion.Core.Interfaces;

/// <summary>
/// Persists and retrieves user activity events and aggregated profiles in Blob Storage.
/// Each user gets a dedicated "folder" (blob prefix) keyed by UserId.
/// Anonymous users are identified by a hashed IP + user-agent fingerprint.
/// </summary>
public interface IActivityRepository
{
    /// <summary>Append activity events to the user's event log (newline-delimited JSON blob).</summary>
    Task AppendEventsAsync(string userId, IEnumerable<ActivityEvent> events, CancellationToken ct = default);

    /// <summary>Load all raw activity events for a user (latest N events, newest first).</summary>
    Task<List<ActivityEvent>> GetRecentEventsAsync(string userId, int maxEvents = 200, CancellationToken ct = default);

    /// <summary>Load the aggregated profile for a user. Returns null if no profile exists yet.</summary>
    Task<UserProfile?> GetProfileAsync(string userId, CancellationToken ct = default);

    /// <summary>Save (overwrite) an aggregated profile for a user.</summary>
    Task SaveProfileAsync(UserProfile profile, CancellationToken ct = default);
}

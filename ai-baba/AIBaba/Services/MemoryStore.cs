using System.Collections.Concurrent;

namespace AIBaba.Services;

/// <summary>
/// In-memory per-user memory: name, preferences, last 10 conversation turns.
/// Indexed by an opaque sessionId minted on first request and stored in a cookie.
/// Swap to SQL/Redis for production multi-instance deployments.
/// </summary>
public interface IMemoryStore
{
    UserProfile GetOrCreate(string sessionId);
    void Append(string sessionId, ChatMessage message);
    void SetName(string sessionId, string name);
    void SetPreferences(string sessionId, string avatar, string mindset);
    void Reset(string sessionId);
}

public class UserProfile
{
    public string SessionId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Avatar { get; set; } = "sage";          // sage | philosopher | healer | elder | storyteller
    public string Mindset { get; set; } = "balanced";     // balanced | logical | spiritual | motivational | creative
    public List<ChatMessage> History { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}

public class MemoryStore : IMemoryStore
{
    private const int MaxHistory = 10;
    private readonly ConcurrentDictionary<string, UserProfile> _profiles = new();

    public UserProfile GetOrCreate(string sessionId)
    {
        return _profiles.GetOrAdd(sessionId, sid => new UserProfile { SessionId = sid });
    }

    public void Append(string sessionId, ChatMessage message)
    {
        var p = GetOrCreate(sessionId);
        p.History.Add(message);
        if (p.History.Count > MaxHistory)
            p.History.RemoveRange(0, p.History.Count - MaxHistory);
        p.LastSeenAt = DateTime.UtcNow;
    }

    public void SetName(string sessionId, string name)
    {
        var p = GetOrCreate(sessionId);
        p.Name = name;
    }

    public void SetPreferences(string sessionId, string avatar, string mindset)
    {
        var p = GetOrCreate(sessionId);
        if (!string.IsNullOrWhiteSpace(avatar))  p.Avatar  = avatar.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(mindset)) p.Mindset = mindset.ToLowerInvariant();
    }

    public void Reset(string sessionId) => _profiles.TryRemove(sessionId, out _);
}

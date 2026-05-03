namespace Experion.Api.Data;

public class ConversationTurn
{
    public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = "user";      // user | assistant
    public string Content { get; set; } = string.Empty;
    public string? IntentType { get; set; }
    public string? ActionKey { get; set; }
    public bool CacheHit { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ActionMapping
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "default";
    public string ActionKey { get; set; } = string.Empty;     // e.g. add_to_cart
    public string Description { get; set; } = string.Empty;   // human description
    public string Phrases { get; set; } = string.Empty;       // pipe-separated examples
    public string? Embedding { get; set; }                    // JSON-serialised float[]
    public string? TargetEndpoint { get; set; }               // optional URL for the worker to invoke
    public bool Enabled { get; set; } = true;
}

public class SemanticCacheEntry
{
    public long Id { get; set; }
    public string TenantId { get; set; } = "default";
    public string NormalisedQuery { get; set; } = string.Empty;
    public string Embedding { get; set; } = string.Empty;     // JSON-serialised float[]
    public string Answer { get; set; } = string.Empty;
    public string? IntentType { get; set; }
    public int Hits { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
}

public class UserProfile
{
    public string UserId { get; set; } = string.Empty;     // PK
    public string TenantId { get; set; } = "default";
    public bool IsAnonymous { get; set; }
    public string? RegionCluster { get; set; }
    public int TotalEvents { get; set; }
    public int TotalSessions { get; set; }
    public string LastActivityJson { get; set; } = "[]";   // last N event summaries
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastNudgeAt { get; set; } = DateTime.MinValue;
}

public class ActivityEventRecord
{
    public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Selector { get; set; }
    public string? Text { get; set; }
    public string? MetaJson { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class AuditLog
{
    public long Id { get; set; }
    public string LogId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string IntentType { get; set; } = string.Empty;
    public string? ActionKey { get; set; }
    public bool CacheHit { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public long ElapsedMs { get; set; }
    public bool? FeedbackPositive { get; set; }
    public string? FeedbackComment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class TenantConfig
{
    public string TenantId { get; set; } = "default";
    public string Name { get; set; } = "Default Tenant";
    public string KbContent { get; set; } = string.Empty;       // demo "knowledge base" text dump
    public string GreetingMessage { get; set; } = string.Empty;
    public int IdleNudgeSeconds { get; set; } = 30;
    public int NudgeCooldownSeconds { get; set; } = 60;
}

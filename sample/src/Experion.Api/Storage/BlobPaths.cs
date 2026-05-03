namespace Experion.Api.Storage;

/// <summary>
/// Centralised, deterministic blob-path conventions.
///   conversations / {tenant} / {userId} / {yyyy-MM-dd}.jsonl
///   activity      / {tenant} / {userId} / {yyyy-MM-dd}.jsonl
/// </summary>
public static class BlobPaths
{
    public const string ConversationsContainer = "conversations";
    public const string ActivityContainer      = "activity";

    public static string Conversation(string tenant, string userId, DateTime utc) =>
        $"{Sanitise(tenant)}/{Sanitise(userId)}/{utc:yyyy-MM-dd}.jsonl";

    public static string Activity(string tenant, string userId, DateTime utc) =>
        $"{Sanitise(tenant)}/{Sanitise(userId)}/{utc:yyyy-MM-dd}.jsonl";

    public static string UserPrefix(string tenant, string userId) =>
        $"{Sanitise(tenant)}/{Sanitise(userId)}/";

    private static string Sanitise(string s) =>
        string.IsNullOrEmpty(s) ? "_" : s.Replace("/", "_").Replace("\\", "_");
}

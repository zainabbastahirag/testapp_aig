namespace AIBaba.Models;

public class AskRequest
{
    public string Message { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Avatar { get; set; }
    public string? Mindset { get; set; }
}

public class AskResponse
{
    public bool Success { get; set; }
    public string Reply { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public string Avatar { get; set; } = "wise";
    public string Mindset { get; set; } = "spiritual";
    public string? Name { get; set; }
    public int HistoryLength { get; set; }
    public long ProcessingMs { get; set; }
}

public class ProfileUpdateRequest
{
    public string? Name { get; set; }
    public string? Avatar { get; set; }
    public string? Mindset { get; set; }
}

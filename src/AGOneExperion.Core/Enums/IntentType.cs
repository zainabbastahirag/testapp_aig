namespace AGOneExperion.Core.Enums;

public enum IntentType
{
    /// <summary>User is browsing and exploring content.</summary>
    Exploration,
    /// <summary>User is searching for a specific solution or product.</summary>
    ProductSearch,
    /// <summary>User wants to understand a concept or feature.</summary>
    Learning,
    /// <summary>User is ready to take an action (buy, sign up, etc.).</summary>
    Decision,
    /// <summary>User circled an area and wants contextual help.</summary>
    ContextualHelp,
    /// <summary>User has a specific question.</summary>
    DirectQuestion,
    /// <summary>Unable to determine intent confidently.</summary>
    Unknown
}

namespace AGOneExperion.Core.Enums;

public enum RecommendationType
{
    /// <summary>Suggest a product or solution.</summary>
    Product,
    /// <summary>Surface a relevant KB article or guide.</summary>
    Content,
    /// <summary>Suggest a next navigation step.</summary>
    Navigation,
    /// <summary>Offer a comparison between options the user viewed.</summary>
    Comparison,
    /// <summary>Proactive popup based on long idle or repeated scroll.</summary>
    ProactiveNudge
}

using AGOneExperion.Core.Models;

namespace AGOneExperion.Core.Interfaces;

/// <summary>
/// Cleans and flattens captured DOM elements into an LLM-friendly text representation.
/// </summary>
public interface IDomFragmentCleaner
{
    CleanedFragment Clean(List<CapturedElement> elements, int maxTokens = 4000);
}

public class CleanedFragment
{
    public string CleanedText { get; set; } = string.Empty;
    public string ContentType { get; set; } = "text";
    public int InteractiveElementCount { get; set; }
    public int TotalElementCount { get; set; }
}

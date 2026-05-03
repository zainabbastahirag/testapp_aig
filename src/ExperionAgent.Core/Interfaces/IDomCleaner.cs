using ExperionAgent.Core.Models;

namespace ExperionAgent.Core.Interfaces;

public interface IDomCleaner
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

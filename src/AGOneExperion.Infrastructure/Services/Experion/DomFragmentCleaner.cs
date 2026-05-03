using System.Text;
using AGOneExperion.Core.Interfaces;
using AGOneExperion.Core.Models;
using Microsoft.Extensions.Logging;

namespace AGOneExperion.Infrastructure.Services.Experion;

/// <summary>
/// Cleans and flattens captured DOM elements into an LLM-friendly text block.
/// </summary>
public class DomFragmentCleaner : IDomFragmentCleaner
{
    private readonly ILogger<DomFragmentCleaner> _log;

    private static readonly HashSet<string> NoiseTags = new(StringComparer.OrdinalIgnoreCase)
    { "SCRIPT", "STYLE", "NOSCRIPT", "IFRAME", "SVG", "PATH", "META", "LINK", "BR", "HR", "WBR" };

    private static readonly HashSet<string> InteractiveTags = new(StringComparer.OrdinalIgnoreCase)
    { "INPUT", "BUTTON", "SELECT", "TEXTAREA", "A", "DETAILS", "SUMMARY" };

    private const int CharsPerToken = 4;

    public DomFragmentCleaner(ILogger<DomFragmentCleaner> log) => _log = log;

    public CleanedFragment Clean(List<CapturedElement> elements, int maxTokens = 4000)
    {
        if (elements == null || elements.Count == 0)
            return new CleanedFragment { CleanedText = "[Empty region]", ContentType = "empty" };

        var filtered = elements
            .Where(e => !NoiseTags.Contains(e.Tag))
            .Where(e => !string.IsNullOrWhiteSpace(e.Text) || e.IsInteractive || IsStructural(e.Tag))
            .ToList();

        var contentType = DetectType(filtered);
        var sb = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var interactiveCount = 0;

        // Headings first
        var headings = filtered.Where(e => e.Tag.Length == 2 && e.Tag[0] == 'H' && char.IsDigit(e.Tag[1])).ToList();
        if (headings.Any())
        {
            sb.AppendLine("## Sections:");
            foreach (var h in headings)
                if (AddUnique(seen, h.Text)) sb.AppendLine($"  [{h.Tag}] {h.Text?.Trim()}");
            sb.AppendLine();
        }

        // Interactive elements
        var interactives = filtered.Where(e => e.IsInteractive || InteractiveTags.Contains(e.Tag)).ToList();
        if (interactives.Any())
        {
            sb.AppendLine("## Interactive Elements:");
            foreach (var ie in interactives)
            {
                interactiveCount++;
                var label = ie.AriaLabel ?? ie.Placeholder ?? ie.Text?.Trim() ?? ie.Id ?? "(unlabeled)";
                sb.AppendLine($"  <{ie.Tag.ToLower()}> [{ie.Type ?? "element"}] \"{label}\"");
            }
            sb.AppendLine();
        }

        // Text content
        var textEls = filtered.Except(headings).Except(interactives).ToList();
        if (textEls.Any())
        {
            sb.AppendLine("## Content:");
            foreach (var te in textEls)
                if (AddUnique(seen, te.Text))
                    sb.AppendLine($"  {Truncate(te.Text, 300)}");
        }

        var result = sb.ToString();
        var maxChars = maxTokens * CharsPerToken;
        if (result.Length > maxChars) result = result[..maxChars] + "\n[truncated]";

        return new CleanedFragment
        {
            CleanedText = result,
            ContentType = contentType,
            InteractiveElementCount = interactiveCount,
            TotalElementCount = filtered.Count
        };
    }

    private static string DetectType(List<CapturedElement> els)
    {
        if (els.Any(e => e.Tag.Equals("TABLE", StringComparison.OrdinalIgnoreCase))) return "data";
        if (els.Any(e => e.Tag.Equals("FORM", StringComparison.OrdinalIgnoreCase))) return "form";
        if (els.Any(e => e.Tag.Equals("NAV", StringComparison.OrdinalIgnoreCase))) return "navigation";
        if (els.Any(e => e.IsInteractive)) return "interactive";
        return "text";
    }

    private static bool IsStructural(string tag) =>
        tag is "TABLE" or "FORM" or "NAV" or "MAIN" or "ARTICLE" or "SECTION" or "ASIDE";

    private static bool AddUnique(HashSet<string> seen, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return seen.Add(text.Trim()[..Math.Min(text.Trim().Length, 200)]);
    }

    private static string? Truncate(string? s, int max) =>
        s == null ? null : s.Length > max ? s[..max] + "..." : s;
}

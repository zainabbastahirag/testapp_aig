using System.Text;
using ExperionAgent.Core.Interfaces;
using ExperionAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace ExperionAgent.Infrastructure.Mining;

public class DomCleanerService : IDomCleaner
{
    private readonly ILogger<DomCleanerService> _log;
    private const int CharsPerToken = 4;

    private static readonly HashSet<string> NoiseTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "SCRIPT", "STYLE", "NOSCRIPT", "IFRAME", "SVG", "PATH", "META", "LINK",
        "BR", "HR", "WBR", "TRACK", "SOURCE", "EMBED", "OBJECT", "PARAM"
    };

    private static readonly HashSet<string> InteractiveTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "INPUT", "BUTTON", "SELECT", "TEXTAREA", "A", "DETAILS", "SUMMARY"
    };

    public DomCleanerService(ILogger<DomCleanerService> log) => _log = log;

    public CleanedFragment Clean(List<CapturedElement> elements, int maxTokens = 4000)
    {
        if (elements == null || elements.Count == 0)
        {
            return new CleanedFragment
            {
                CleanedText = "[Empty region — no elements captured]",
                ContentType = "empty"
            };
        }

        var filtered = elements
            .Where(e => !NoiseTags.Contains(e.Tag))
            .Where(e => !string.IsNullOrWhiteSpace(e.Text) || e.IsInteractive || IsStructural(e.Tag))
            .ToList();

        var sb = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var interactiveCount = 0;
        var maxChars = maxTokens * CharsPerToken;

        var headings = filtered.Where(e => e.Tag.StartsWith("H") && e.Tag.Length == 2 && char.IsDigit(e.Tag[1])).ToList();
        var interactives = filtered.Where(e => e.IsInteractive || InteractiveTags.Contains(e.Tag)).ToList();
        var rest = filtered.Except(headings).Except(interactives).ToList();

        if (headings.Any())
        {
            sb.AppendLine("## Sections:");
            foreach (var h in headings)
                if (TryAddUnique(seen, h.Text))
                    sb.AppendLine($"  - [{h.Tag}] {h.Text?.Trim()}");
            sb.AppendLine();
        }

        if (interactives.Any())
        {
            sb.AppendLine("## Interactive:");
            foreach (var ie in interactives)
            {
                interactiveCount++;
                var label = ie.AriaLabel ?? ie.Placeholder ?? ie.Text?.Trim() ?? ie.Id ?? "(unlabeled)";
                sb.AppendLine($"  - <{ie.Tag.ToLower()}> [{ie.Type ?? "element"}] \"{label}\"");
            }
            sb.AppendLine();
        }

        if (rest.Any())
        {
            sb.AppendLine("## Content:");
            foreach (var te in rest)
                if (TryAddUnique(seen, te.Text))
                    sb.AppendLine($"  {Truncate(te.Text, 300)}");
        }

        var result = sb.ToString();
        if (result.Length > maxChars)
            result = result[..maxChars] + "\n[... truncated]";

        return new CleanedFragment
        {
            CleanedText = result,
            ContentType = DetectContentType(filtered),
            InteractiveElementCount = interactiveCount,
            TotalElementCount = filtered.Count
        };
    }

    private static string DetectContentType(List<CapturedElement> els)
    {
        if (els.Any(e => e.Tag.Equals("TABLE", StringComparison.OrdinalIgnoreCase))) return "data";
        if (els.Any(e => e.Tag.Equals("FORM", StringComparison.OrdinalIgnoreCase))) return "form";
        if (els.Any(e => e.Tag.Equals("NAV", StringComparison.OrdinalIgnoreCase) || e.Role == "navigation")) return "navigation";
        if (els.Any(e => e.IsInteractive)) return "interactive";
        return "text";
    }

    private static bool IsStructural(string tag) =>
        tag is "TABLE" or "FORM" or "NAV" or "MAIN" or "ARTICLE" or "SECTION";

    private static bool TryAddUnique(HashSet<string> seen, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return seen.Add(text.Trim()[..Math.Min(text.Trim().Length, 200)]);
    }

    private static string? Truncate(string? text, int max) =>
        text == null ? null : text.Trim().Length > max ? text.Trim()[..max] + "..." : text.Trim();
}

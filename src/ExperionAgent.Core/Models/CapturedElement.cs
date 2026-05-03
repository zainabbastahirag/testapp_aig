namespace ExperionAgent.Core.Models;

public class CapturedElement
{
    public string Tag { get; set; } = string.Empty;
    public string? Id { get; set; }
    public string? Text { get; set; }
    public string? Type { get; set; }
    public string? Href { get; set; }
    public string? Src { get; set; }
    public string? Alt { get; set; }
    public string? ClassName { get; set; }
    public string? Role { get; set; }
    public string? AriaLabel { get; set; }
    public string? Placeholder { get; set; }
    public string? Value { get; set; }
    public bool IsInteractive { get; set; }
    public ElementRect? Rect { get; set; }
}

public class ElementRect
{
    public double Top { get; set; }
    public double Left { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

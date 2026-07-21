namespace AI.Assistant.Core.Models;

public class ExtractedFact
{
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = FactCategory.Other;
    public double Importance { get; set; } = 0.5;
    public string SourceMessageId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

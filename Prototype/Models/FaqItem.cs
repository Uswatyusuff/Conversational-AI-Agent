namespace CouncilChatbotPrototype.Models;

public class FaqItem
{
    public string Service { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> Keywords { get; set; } = new();
    public string Answer { get; set; } = "";
    public string NextStepsUrl { get; set; } = "";

    // New: optional response variations
    public List<string>? Responses { get; set; }
}
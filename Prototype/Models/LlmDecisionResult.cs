namespace CouncilChatbotPrototype.Models;

public class LlmDecisionResult
{
    public string Action { get; set; } = "clarify";   
    public string Service { get; set; } = "Unknown";
    public string Intent { get; set; } = "";
    public string Reply { get; set; } = "";
    public string NextStepsUrl { get; set; } = "";
}
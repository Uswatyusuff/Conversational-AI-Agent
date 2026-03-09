namespace CouncilChatbotPrototype.Models;

public class RouteDecision
{
    public string Mode { get; set; } = "faq"; // faq | signpost | crisis | out_of_scope
    public string Service { get; set; } = "Unknown";
    public string Intent { get; set; } = "";
    public string Reply { get; set; } = "";
    public string NextStepsUrl { get; set; } = "";
}
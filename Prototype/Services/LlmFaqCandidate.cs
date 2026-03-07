namespace CouncilChatbotPrototype.Models;

public class LlmFaqCandidate
{
    public string Service { get; set; } = "";
    public string Title { get; set; } = "";
    public string Answer { get; set; } = "";
    public List<string> Responses { get; set; } = new();
    public string NextStepsUrl { get; set; } = "";
    public float Score { get; set; }
}
namespace CouncilChatbotPrototype.Models;

public class ChatResponse
{
    public string       Reply        { get; set; } = "";
    public string       Service      { get; set; } = "Unknown";
    public string       NextStepsUrl { get; set; } = "";
    public List<string> Suggestions  { get; set; } = new();
}
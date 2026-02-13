namespace CouncilChatbotPrototype.Models;

public class ChatRequest
{
    public string Message { get; set; } = "";
    public string? SessionId { get; set; } = null;
}
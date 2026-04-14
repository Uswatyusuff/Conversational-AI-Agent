namespace CouncilChatbotPrototype.Models;

public class FeedbackRequest
{
    public string Service { get; set; } = "Unknown";
    public string Helpful { get; set; } = "Unknown"; // Yes/No
    public string Comment { get; set; } = "";
    public string? SessionId { get; set; } = null;
}
namespace CouncilChatbotPrototype.Models;

public class FaqChunk
{
    public string Id { get; set; } = "";
    public string Service { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string NextStepsUrl { get; set; } = "";
    public float[] Vector { get; set; } = Array.Empty<float>();
}
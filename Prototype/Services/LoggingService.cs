using System.Text.Json;

namespace CouncilChatbotPrototype.Services;

public class LoggingService
{
    public async Task LogChatAsync(object obj)
        => await AppendJsonLine("Logs/chatlog.jsonl", obj);

    public async Task LogFeedbackAsync(object obj)
        => await AppendJsonLine("Logs/feedback.jsonl", obj);

    private static async Task AppendJsonLine(string path, object obj)
    {
        var line = JsonSerializer.Serialize(obj);
        await File.AppendAllTextAsync(path, line + Environment.NewLine);
    }
}
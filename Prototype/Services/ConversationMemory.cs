using System.Collections.Concurrent;

namespace CouncilChatbotPrototype.Services;

public class ConversationMemory
{
    private readonly ConcurrentDictionary<string, string> _sessionMemory = new();

    public string GetLastService(string sessionId)
        => _sessionMemory.TryGetValue(sessionId, out var svc) ? (svc ?? "") : "";

    public void SetLastService(string sessionId, string service)
    {
        if (!string.IsNullOrWhiteSpace(service) && service != "Unknown")
            _sessionMemory[sessionId] = service;
    }
}
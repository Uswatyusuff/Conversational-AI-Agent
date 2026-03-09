using System.Collections.Concurrent;

namespace CouncilChatbotPrototype.Services;

public class ConversationMemory
{
    private readonly ConcurrentDictionary<string, string> _sessionLastService = new();
    private readonly ConcurrentDictionary<string, string> _sessionMode = new();

    public string GetLastService(string sessionId)
        => _sessionLastService.TryGetValue(sessionId, out var svc) ? (svc ?? "") : "";

    public void SetLastService(string sessionId, string service)
    {
        if (!string.IsNullOrWhiteSpace(service) && service != "Unknown")
            _sessionLastService[sessionId] = service;
    }

    public string GetMode(string sessionId)
        => _sessionMode.TryGetValue(sessionId, out var mode) ? (mode ?? "") : "";

    public void SetMode(string sessionId, string mode)
    {
        if (!string.IsNullOrWhiteSpace(mode))
            _sessionMode[sessionId] = mode;
    }

    public void ClearMode(string sessionId)
    {
        _sessionMode.TryRemove(sessionId, out _);
    }
}
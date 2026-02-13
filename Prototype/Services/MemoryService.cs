namespace CouncilChatbotPrototype.Services;

public class MemoryService
{
    private readonly Dictionary<string, string> _sessionLastService = new();

    public string GetLastService(string sessionId)
    {
        if (_sessionLastService.TryGetValue(sessionId, out var svc))
            return svc;
        return "Unknown";
    }

    public void SetLastService(string sessionId, string service)
    {
        _sessionLastService[sessionId] = service;
    }
}
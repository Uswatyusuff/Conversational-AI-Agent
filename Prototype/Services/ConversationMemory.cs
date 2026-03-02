using System.Collections.Concurrent;
using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

public class ConversationMemory
{
    private class SessionState
    {
        public string LastService { get; set; } = "";
        public List<PendingChoice> PendingChoices { get; set; } = new();
    }

    public class PendingChoice
    {
        public FaqChunk Chunk { get; set; } = new();
        public float Score { get; set; }
    }

    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

    private SessionState GetState(string sessionId)
        => _sessions.GetOrAdd(sessionId, _ => new SessionState());

    // ----- Last service -----
    public string GetLastService(string sessionId)
    {
        var state = GetState(sessionId);
        return state.LastService ?? "";
    }

    public void SetLastService(string sessionId, string service)
    {
        if (string.IsNullOrWhiteSpace(service) || service == "Unknown")
            return;

        var state = GetState(sessionId);
        state.LastService = service;
    }

    // ----- Pending disambiguation choices (1/2 selection) -----
    public void SetPendingChoices(string sessionId, List<PendingChoice> choices)
    {
        var state = GetState(sessionId);
        state.PendingChoices = choices ?? new List<PendingChoice>();
    }

    public List<PendingChoice> GetPendingChoices(string sessionId)
    {
        var state = GetState(sessionId);
        return state.PendingChoices ?? new List<PendingChoice>();
    }

    public void ClearPendingChoices(string sessionId)
    {
        var state = GetState(sessionId);
        state.PendingChoices = new List<PendingChoice>();
    }
}
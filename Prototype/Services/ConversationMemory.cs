using System.Collections.Concurrent;
using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

public class ConversationMemory
{
    public class ChatTurn
    {
        public string Role { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime Ts { get; set; } = DateTime.UtcNow;
    }

    public class PendingChoice
    {
        public FaqChunk Chunk { get; set; } = new();
        public float Score { get; set; }
    }

    private class SessionState
    {
        public string LastService { get; set; } = "";
        public List<PendingChoice> PendingChoices { get; set; } = new();
        public List<ChatTurn> Turns { get; set; } = new();
    }

    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

    private SessionState GetState(string sessionId)
        => _sessions.GetOrAdd(sessionId, _ => new SessionState());

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

    public void AddTurn(string sessionId, string role, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var state = GetState(sessionId);

        state.Turns.Add(new ChatTurn
        {
            Role = role,
            Message = message,
            Ts = DateTime.UtcNow
        });

        if (state.Turns.Count > 10)
            state.Turns = state.Turns.TakeLast(10).ToList();
    }

    public List<ChatTurn> GetRecentTurns(string sessionId, int take = 6)
    {
        var state = GetState(sessionId);
        return state.Turns.TakeLast(take).ToList();
    }
}
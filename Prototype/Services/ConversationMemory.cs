using System.Collections.Concurrent;
using System.Text.RegularExpressions;
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
        public string LastIntent { get; set; } = "";
        public string PendingFlow { get; set; } = "";

        public string LastPostcodeMasked { get; set; } = "";
        public string LastAddressMasked { get; set; } = "";

        public string ActivePostcode { get; set; } = "";
        public string ActiveAddress { get; set; } = "";
        public string LastBinResult { get; set; } = "";
        public bool HasSelectedAddress { get; set; } = false;

        public List<string> LastSuggestions { get; set; } = new();
        public List<PendingChoice> PendingChoices { get; set; } = new();
        public List<ChatTurn> Turns { get; set; } = new();
        public DateTime LastTouchedUtc { get; set; } = DateTime.UtcNow;

        // ── Appointment booking flow state ────────────────────────────────────
        public string AppointmentType  { get; set; } = "";
        public string AppointmentDate  { get; set; } = "";
        public string AppointmentTime  { get; set; } = "";
        public string AppointmentName  { get; set; } = "";
        public string AppointmentPhone { get; set; } = "";
        public string AppointmentEmail { get; set; } = "";

        // ── Council Tax calculator flow state ─────────────────────────────────
        public decimal CtaxMonthlyBill { get; set; } = 0m;

        // ── Location lookup state ─────────────────────────────────────────────
        public string LocationLookupType { get; set; } = "all"; // "all"|"library"|"recycling_centre"|"council_office"|"school"

        // ── Housing navigator state ───────────────────────────────────────────
        public string HousingFlowNode { get; set; } = "";
    }

    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(30);

    private SessionState GetState(string sessionId)
    {
        CleanupExpiredSessions();

        var state = _sessions.GetOrAdd(sessionId, _ => new SessionState());
        state.LastTouchedUtc = DateTime.UtcNow;
        return state;
    }

    private void CleanupExpiredSessions()
    {
        var now = DateTime.UtcNow;

        foreach (var kvp in _sessions)
        {
            if (now - kvp.Value.LastTouchedUtc > SessionTtl)
            {
                _sessions.TryRemove(kvp.Key, out _);
            }
        }
    }

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

    public string GetLastIntent(string sessionId)
    {
        var state = GetState(sessionId);
        return state.LastIntent ?? "";
    }

    public void SetLastIntent(string sessionId, string intent)
    {
        var state = GetState(sessionId);
        state.LastIntent = intent ?? "";
    }

    public string GetPendingFlow(string sessionId)
    {
        var state = GetState(sessionId);
        return state.PendingFlow ?? "";
    }

    public void SetPendingFlow(string sessionId, string pendingFlow)
    {
        var state = GetState(sessionId);
        state.PendingFlow = pendingFlow ?? "";
    }

    public void ClearPendingFlow(string sessionId)
    {
        var state = GetState(sessionId);
        state.PendingFlow = "";
    }

    public string GetMaskedPostcode(string sessionId)
    {
        var state = GetState(sessionId);
        return state.LastPostcodeMasked ?? "";
    }

    public void SetMaskedPostcode(string sessionId, string postcode)
    {
        var state = GetState(sessionId);
        state.LastPostcodeMasked = MaskPostcode(postcode);
    }

    public string GetMaskedAddress(string sessionId)
    {
        var state = GetState(sessionId);
        return state.LastAddressMasked ?? "";
    }

    public void SetMaskedAddress(string sessionId, string address)
    {
        var state = GetState(sessionId);
        state.LastAddressMasked = MaskAddress(address);
    }

    public void ClearAddressContext(string sessionId)
{
    var state = GetState(sessionId);

    state.LastPostcodeMasked = "";
    state.LastAddressMasked = "";

    state.ActivePostcode = "";
    state.ActiveAddress = "";
    state.LastBinResult = "";
    state.HasSelectedAddress = false;
}

    public void SetLastSuggestions(string sessionId, List<string> suggestions)
    {
        var state = GetState(sessionId);
        state.LastSuggestions = suggestions ?? new List<string>();
    }

    public List<string> GetLastSuggestions(string sessionId)
    {
        var state = GetState(sessionId);
        return state.LastSuggestions?.ToList() ?? new List<string>();
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
            Message = SanitizeForMemory(message),
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

    public void SetActivePostcode(string sessionId, string postcode)
    {
        var state = GetState(sessionId);
        state.ActivePostcode = postcode ?? "";
    }

    public string GetActivePostcode(string sessionId)
    {
        var state = GetState(sessionId);
        return state.ActivePostcode ?? "";
    }

    public void SetActiveAddress(string sessionId, string address)
    {
        var state = GetState(sessionId);
        state.ActiveAddress = address ?? "";
    }

    public string GetActiveAddress(string sessionId)
    {
        var state = GetState(sessionId);
        return state.ActiveAddress ?? "";
    }

    public void SetLastBinResult(string sessionId, string result)
    {
        var state = GetState(sessionId);
        state.LastBinResult = result ?? "";
    }

    public string GetLastBinResult(string sessionId)
    {
        var state = GetState(sessionId);
        return state.LastBinResult ?? "";
    }

    public void SetHasSelectedAddress(string sessionId, bool value)
    {
        var state = GetState(sessionId);
        state.HasSelectedAddress = value;
    }

    public bool GetHasSelectedAddress(string sessionId)
    {
        var state = GetState(sessionId);
        return state.HasSelectedAddress;
    }

    private static string SanitizeForMemory(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "";

        var output = input;

        output = Regex.Replace(
            output,
            @"\b[A-Z]{1,2}\d[A-Z\d]?\s?\d[A-Z]{2}\b",
            "[POSTCODE]",
            RegexOptions.IgnoreCase);

        output = Regex.Replace(
            output,
            @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
            "[EMAIL]",
            RegexOptions.IgnoreCase);

        output = Regex.Replace(
            output,
            @"\b(?:\+44|0)\d[\d\s]{8,}\b",
            "[PHONE]",
            RegexOptions.IgnoreCase);

        output = Regex.Replace(
            output,
            @"\b(my name is|i am|i'm)\s+[a-z][a-z\s'-]*",
            "$1 [NAME]",
            RegexOptions.IgnoreCase);

        return output;
    }

    private static string MaskPostcode(string postcode)
    {
        if (string.IsNullOrWhiteSpace(postcode))
            return "";

        var cleaned = postcode.Trim().ToUpperInvariant();
        if (cleaned.Length <= 3)
            return "[POSTCODE]";

        return $"[POSTCODE:{cleaned[^3..]}]";
    }

    private static string MaskAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return "";

        return "[ADDRESS_SELECTED]";
    }

    // ── Appointment flow accessors ────────────────────────────────────────────────

    public void SetAppointmentType(string sessionId, string type)
        => GetState(sessionId).AppointmentType = type ?? "";

    public string GetAppointmentType(string sessionId)
        => GetState(sessionId).AppointmentType ?? "";

    public void SetAppointmentDate(string sessionId, string date)
        => GetState(sessionId).AppointmentDate = date ?? "";

    public string GetAppointmentDate(string sessionId)
        => GetState(sessionId).AppointmentDate ?? "";

    public void SetAppointmentTime(string sessionId, string time)
        => GetState(sessionId).AppointmentTime = time ?? "";

    public string GetAppointmentTime(string sessionId)
        => GetState(sessionId).AppointmentTime ?? "";

    public void SetAppointmentName(string sessionId, string name)
        => GetState(sessionId).AppointmentName = SanitizeForMemory(name ?? "");

    public string GetAppointmentName(string sessionId)
        => GetState(sessionId).AppointmentName ?? "";

    public void SetAppointmentPhone(string sessionId, string phone)
        => GetState(sessionId).AppointmentPhone = SanitizeForMemory(phone ?? "");

    public string GetAppointmentPhone(string sessionId)
        => GetState(sessionId).AppointmentPhone ?? "";

    public void SetAppointmentEmail(string sessionId, string email)
        => GetState(sessionId).AppointmentEmail = SanitizeForMemory(email ?? "");

    public string GetAppointmentEmail(string sessionId)
        => GetState(sessionId).AppointmentEmail ?? "";

    public void ClearAppointmentFlow(string sessionId)
    {
        var s = GetState(sessionId);
        s.AppointmentType  = "";
        s.AppointmentDate  = "";
        s.AppointmentTime  = "";
        s.AppointmentName  = "";
        s.AppointmentPhone = "";
        s.AppointmentEmail = "";
    }

    // ── Council Tax calculator accessors ─────────────────────────────────────────

    public void SetCtaxMonthlyBill(string sessionId, decimal amount)
        => GetState(sessionId).CtaxMonthlyBill = amount;

    public decimal GetCtaxMonthlyBill(string sessionId)
        => GetState(sessionId).CtaxMonthlyBill;

    // ── Location lookup accessors ─────────────────────────────────────────────────

    public void SetLocationLookupType(string sessionId, string type)
        => GetState(sessionId).LocationLookupType = type ?? "all";

    public string GetLocationLookupType(string sessionId)
        => GetState(sessionId).LocationLookupType ?? "all";

    // ── Housing navigator accessors ───────────────────────────────────────────────

    public void SetHousingFlowNode(string sessionId, string node)
        => GetState(sessionId).HousingFlowNode = node ?? "";

    public string GetHousingFlowNode(string sessionId)
        => GetState(sessionId).HousingFlowNode ?? "";
}
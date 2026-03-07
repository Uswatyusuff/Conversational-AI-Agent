public record LlmChatResult(
    string action,          // "answer" | "clarify"
    string reply,
    string service,
    string nextStepsUrl
);
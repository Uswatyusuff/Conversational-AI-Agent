using Microsoft.AspNetCore.Mvc;
using CouncilChatbotPrototype.Services;
using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Controllers;

[ApiController]
public class ChatController : ControllerBase
{
    private readonly ChatOrchestrator _chat;
    private readonly LoggingService _logging;

    public ChatController(ChatOrchestrator chat, LoggingService logging)
    {
        _chat = chat;
        _logging = logging;
    }

    [HttpPost("/api/chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest body)
    {
        var message = body?.Message?.Trim() ?? "";
        var sessionId = body?.SessionId?.Trim();
        if (string.IsNullOrWhiteSpace(sessionId)) sessionId = "default";

        if (string.IsNullOrWhiteSpace(message))
            return BadRequest(new { reply = "Please type a question.", service = "Unknown", nextStepsUrl = "" });

        var result = await _chat.HandleChatAsync(sessionId, message);

        await _logging.LogChatAsync(new
        {
            ts = DateTime.UtcNow,
            sessionId,
            userMessage = message,
            matchedService = result.service,
            score = result.score
        });

        return Ok(new { reply = result.reply, service = result.service, nextStepsUrl = result.nextStepsUrl });
    }

    [HttpPost("/api/feedback")]
    public async Task<IActionResult> Feedback([FromBody] FeedbackRequest body)
    {
        await _logging.LogFeedbackAsync(new
        {
            ts = DateTime.UtcNow,
            service = body?.Service ?? "Unknown",
            helpful = body?.Helpful ?? "Unknown",
            comment = body?.Comment ?? "",
            sessionId = body?.SessionId
        });

        return Ok(new { status = "saved" });
    }
}
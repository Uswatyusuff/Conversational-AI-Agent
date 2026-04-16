using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.RegularExpressions;
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
    [EnableRateLimiting("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest body)
    {
        var message = body?.Message?.Trim() ?? "";
        var sessionId = body?.SessionId?.Trim();
        // Never fall back to a shared "default" session — that would let messages from
        // one user (or browser tab) bleed into another user's conversation history.
        // A missing sessionId means the client didn't send one; treat it as a fresh
        // anonymous session rather than contaminating a global singleton.
        if (string.IsNullOrWhiteSpace(sessionId)) sessionId = Guid.NewGuid().ToString();

        if (string.IsNullOrWhiteSpace(message))
            return BadRequest(new
            {
                reply = "Please type a question.",
                service = "Unknown",
                nextStepsUrl = "",
                suggestions = new List<string>()
            });

        var result = await _chat.HandleChatAsync(sessionId, message);

        await _logging.LogChatAsync(new
        {
            ts = DateTime.UtcNow,
            sessionId,
            userMessage = SanitizeForLog(message),
            matchedService = result.service,
            score = result.score
        });

        return Ok(new
        {
            reply = result.reply,
            service = result.service,
            nextStepsUrl = result.nextStepsUrl,
            suggestions = result.suggestions
        });
    }

    [HttpPost("/api/feedback")]
    public async Task<IActionResult> Feedback([FromBody] FeedbackRequest body)
    {
        await _logging.LogFeedbackAsync(new
        {
            ts = DateTime.UtcNow,
            service = body?.Service ?? "Unknown",
            helpful = body?.Helpful ?? "Unknown",
            comment = SanitizeForLog(body?.Comment ?? ""),
            sessionId = body?.SessionId
        });

        return Ok(new { status = "saved" });
    }

    private static string SanitizeForLog(string input)
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

        return output;
    }
}
using Microsoft.AspNetCore.Mvc;
using CouncilChatbotPrototype.Models;
using CouncilChatbotPrototype.Services;

namespace CouncilChatbotPrototype.Controllers;

/// <summary>
/// Exposes guided form-flow endpoints for the chat UI.
///
/// The form flow can be driven either through the chat (via ChatOrchestrator)
/// or directly via these API endpoints for richer form UI interactions.
///
/// POST /api/form/start    → starts a new form session, returns first question
/// POST /api/form/step     → submits an answer, returns next question or summary
/// GET  /api/form/types    → lists available form types
/// DELETE /api/form/cancel → cancels the active form session
/// </summary>
[ApiController]
public class FormFlowController : ControllerBase
{
    private readonly FormFlowService _formFlow;

    public FormFlowController(FormFlowService formFlow)
    {
        _formFlow = formFlow;
    }

    [HttpGet("/api/form/types")]
    public IActionResult GetTypes()
    {
        var types = new[]
        {
            new { key = "benefits",           label = "Housing Benefit & Council Tax Support" },
            new { key = "housing",            label = "Housing Application" },
            new { key = "school",             label = "School Place Application" },
            new { key = "council_tax_change", label = "Council Tax Change of Circumstances" },
            new { key = "blue_badge",         label = "Blue Badge Application" },
        };

        return Ok(new { types });
    }

    [HttpPost("/api/form/start")]
    public IActionResult Start([FromBody] FormStartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.SessionId))
            return BadRequest(new { error = "SessionId is required." });

        if (string.IsNullOrWhiteSpace(request.FormType))
            return BadRequest(new { error = "FormType is required." });

        var response = _formFlow.StartForm(request.SessionId, request.FormType.ToLowerInvariant().Trim());
        return Ok(response);
    }

    [HttpPost("/api/form/step")]
    public IActionResult Step([FromBody] FormStepRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.SessionId))
            return BadRequest(new { error = "SessionId is required." });

        var response = _formFlow.SubmitAnswer(request.SessionId, request.Answer ?? "");
        return Ok(response);
    }

    [HttpDelete("/api/form/cancel")]
    public IActionResult Cancel([FromQuery] string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "SessionId is required." });

        _formFlow.ClearSession(sessionId);
        return Ok(new { message = "Form session cancelled." });
    }
}

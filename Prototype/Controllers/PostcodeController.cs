using Microsoft.AspNetCore.Mvc;
using CouncilChatbotPrototype.Services;

namespace CouncilChatbotPrototype.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PostcodeController : ControllerBase
{
    private readonly PlaywrightService _playwrightService;
    private readonly ConversationMemory _memory;

    public PostcodeController(
        PlaywrightService playwrightService,
        ConversationMemory memory)
    {
        _playwrightService = playwrightService;
        _memory = memory;
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string postcode, [FromQuery] string? sessionId = null)
    {
        if (string.IsNullOrWhiteSpace(postcode))
            return BadRequest(new { error = "Postcode is required." });

        var result = await _playwrightService.GetAddressesByPostcodeAsync(postcode);

        if (!string.IsNullOrWhiteSpace(result.Error))
            return BadRequest(result);

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            _memory.SetMaskedPostcode(sessionId, postcode);
            _memory.SetPendingFlow(sessionId, "awaiting_address_selection");
            _memory.SetLastService(sessionId, "Waste & Bins");
            _memory.SetLastIntent(sessionId, "postcode_lookup");
        }

        return Ok(result);
    }

    [HttpGet("bin-result")]
    public async Task<IActionResult> GetBinResult(
        [FromQuery] string postcode,
        [FromQuery] string address,
        [FromQuery] string? sessionId = null)
    {
        if (string.IsNullOrWhiteSpace(postcode))
            return BadRequest(new { error = "Postcode is required." });

        if (string.IsNullOrWhiteSpace(address))
            return BadRequest(new { error = "Address is required." });

        var result = await _playwrightService.GetBinResultForAddressAsync(postcode, address);

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            _memory.SetMaskedPostcode(sessionId, postcode);
            _memory.SetMaskedAddress(sessionId, address);
            _memory.SetPendingFlow(sessionId, "address_selected");
            _memory.SetLastService(sessionId, "Waste & Bins");
            _memory.SetLastIntent(sessionId, "bin_result");
            // SAVE SESSION STATE
            _memory.SetActivePostcode(sessionId, postcode);
            _memory.SetActiveAddress(sessionId, address);
            _memory.SetLastBinResult(sessionId, result ?? "");
            _memory.SetHasSelectedAddress(sessionId, true);
        }

        return Ok(new
        {
            postcode,
            address,
            result
        });
    }
    
}
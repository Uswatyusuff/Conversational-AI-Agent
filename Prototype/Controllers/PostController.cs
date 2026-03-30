using Microsoft.AspNetCore.Mvc;
using CouncilChatbotPrototype.Services;

namespace CouncilChatbotPrototype.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PostcodeController : ControllerBase
{
    private readonly PlaywrightService _playwrightService;

    public PostcodeController(PlaywrightService playwrightService)
    {
        _playwrightService = playwrightService;
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string postcode)
    {
        if (string.IsNullOrWhiteSpace(postcode))
            return BadRequest(new { error = "Postcode is required." });

        var result = await _playwrightService.GetAddressesByPostcodeAsync(postcode);

        if (!string.IsNullOrWhiteSpace(result.Error))
            return BadRequest(result);

        return Ok(result);
    }

    [HttpGet("bin-result")]
    public async Task<IActionResult> GetBinResult([FromQuery] string postcode, [FromQuery] string address)
    {
        if (string.IsNullOrWhiteSpace(postcode))
            return BadRequest(new { error = "Postcode is required." });

        if (string.IsNullOrWhiteSpace(address))
            return BadRequest(new { error = "Address is required." });

        var result = await _playwrightService.GetBinResultForAddressAsync(postcode, address);

        return Ok(new
        {
            postcode,
            address,
            result
        });
    }
}
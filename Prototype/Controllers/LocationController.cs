using Microsoft.AspNetCore.Mvc;
using CouncilChatbotPrototype.Services;

namespace CouncilChatbotPrototype.Controllers;

/// <summary>
/// Exposes location-based lookup endpoints.
///
/// GET /api/location/nearby?postcode=BD3+8PX&type=all
///   Returns nearest council offices, libraries, recycling centres for a postcode.
///   type = "all" | "council_office" | "library" | "recycling_centre"
///
/// Frontend calls this when the chat signal LOCATION_LOOKUP::{postcode} is received.
/// </summary>
[ApiController]
public class LocationController : ControllerBase
{
    private readonly LocationService _location;

    public LocationController(LocationService location)
    {
        _location = location;
    }

    [HttpGet("/api/location/nearby")]
    public async Task<IActionResult> Nearby(
        [FromQuery] string postcode, [FromQuery] string type = "all")
    {
        if (string.IsNullOrWhiteSpace(postcode))
            return BadRequest(new { error = "Please provide a postcode." });

        var result = await _location.GetNearbyAsync(
            postcode.Trim(), type.Trim().ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(result.Error))
            return BadRequest(new { error = result.Error });

        return Ok(result);
    }
}

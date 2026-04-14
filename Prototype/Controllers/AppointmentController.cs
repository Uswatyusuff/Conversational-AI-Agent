using Microsoft.AspNetCore.Mvc;
using CouncilChatbotPrototype.Models;
using CouncilChatbotPrototype.Services;

namespace CouncilChatbotPrototype.Controllers;

/// <summary>
/// Exposes appointment-related API endpoints.
///
/// The main booking flow is chat-driven (via ChatOrchestrator).
/// These endpoints support the frontend when it needs to display
/// available types, dates, or confirm a booking independently.
///
/// GET  /api/appointment/types         → list of appointment types
/// GET  /api/appointment/slots         → available dates + times (MOCK)
/// POST /api/appointment/confirm       → confirm a booking, returns reference
/// GET  /api/appointment/lookup?ref=   → look up an existing booking
/// </summary>
[ApiController]
public class AppointmentController : ControllerBase
{
    private readonly AppointmentService _appointments;

    public AppointmentController(AppointmentService appointments)
    {
        _appointments = appointments;
    }

    [HttpGet("/api/appointment/types")]
    public IActionResult GetTypes()
    {
        return Ok(new
        {
            types = AppointmentService.AppointmentTypes.Select(t => new
            {
                t.Id,
                t.Name,
                t.Description,
                t.DurationMinutes
            }).ToList()
        });
    }

    [HttpGet("/api/appointment/slots")]
    public IActionResult GetSlots([FromQuery] string? appointmentType = null)
    {
        var dates = _appointments.GetAvailableDates(5);
        var times = _appointments.GetAvailableTimes();

        return Ok(new
        {
            appointmentType = appointmentType ?? "General Enquiry",
            availableDates  = dates,
            availableTimes  = times,
            note            = "MOCK: All slots shown as available. Connect to real council booking API when available."
        });
    }

    [HttpPost("/api/appointment/confirm")]
    public IActionResult Confirm([FromBody] AppointmentBookingData data)
    {
        if (string.IsNullOrWhiteSpace(data?.AppointmentType))
            return BadRequest(new { error = "Appointment type is required." });

        if (string.IsNullOrWhiteSpace(data.Date))
            return BadRequest(new { error = "Date is required." });

        if (string.IsNullOrWhiteSpace(data.Time))
            return BadRequest(new { error = "Time is required." });

        var confirmation = _appointments.ConfirmBooking(data);
        return Ok(confirmation);
    }

    [HttpGet("/api/appointment/lookup")]
    public IActionResult Lookup([FromQuery] string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return BadRequest(new { error = "Reference number is required." });

        var booking = _appointments.GetBooking(reference.Trim().ToUpperInvariant());
        if (booking == null)
            return NotFound(new { error = $"No booking found for reference '{reference}'." });

        return Ok(booking);
    }
}

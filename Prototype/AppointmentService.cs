using System.Collections.Concurrent;
using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

/// <summary>
/// Manages in-chat appointment booking flows.
///
/// The booking process is entirely conversation-driven — no separate booking UI page
/// is required. State is stored per-session in memory (30-min TTL matches ConversationMemory).
///
/// MOCK: Slot availability and confirmation are simulated.
/// READY FOR REAL API: Replace GenerateSlots() and ConfirmBooking() with real council booking API calls.
/// </summary>
public class AppointmentService
{
    // ── Available appointment types ──────────────────────────────────────────────
    public static readonly List<AppointmentTypeInfo> AppointmentTypes = new()
    {
        new() { Id = "council_tax",  Name = "Council Tax Enquiry", Description = "Questions about your bill, discounts, or arrears.", DurationMinutes = 30 },
        new() { Id = "housing",      Name = "Housing Advice",       Description = "Help with finding a home or housing issues.",       DurationMinutes = 45 },
        new() { Id = "benefits",     Name = "Benefits & Support",   Description = "Guidance on benefits, Blue Badge, or hardship.",    DurationMinutes = 45 },
        new() { Id = "planning",     Name = "Planning Enquiry",     Description = "Pre-application planning advice.",                  DurationMinutes = 30 },
        new() { Id = "general",      Name = "General Enquiry",      Description = "Any other council service question.",              DurationMinutes = 30 },
    };

    // ── Available time slots per day (MOCK) ──────────────────────────────────────
    private static readonly string[] TimeSlots =
    {
        "9:00 AM", "9:30 AM", "10:00 AM", "10:30 AM",
        "11:00 AM", "11:30 AM", "2:00 PM",  "2:30 PM",
        "3:00 PM",  "3:30 PM",  "4:00 PM"
    };

    // ── In-memory booking store (MOCK — replace with real DB/API) ────────────────
    private readonly ConcurrentDictionary<string, BookingConfirmation> _bookings = new();

    // ─────────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the list of appointment type names for display as suggestion chips.
    /// </summary>
    public List<string> GetAppointmentTypeNames()
        => AppointmentTypes.Select(t => t.Name).ToList();

    /// <summary>
    /// Resolves a user's chip selection (e.g. "Council Tax Enquiry") to an AppointmentTypeInfo.
    /// </summary>
    public AppointmentTypeInfo? ResolveType(string input)
    {
        input = input?.Trim() ?? "";
        return AppointmentTypes.FirstOrDefault(t =>
            string.Equals(t.Name, input, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Id,   input, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Generates the next 5 working day labels (Mon–Fri, skipping weekends).
    /// MOCK: No real availability check — all days returned as available.
    /// </summary>
    public List<string> GetAvailableDates(int count = 5)
    {
        var dates = new List<string>();
        var cursor = DateTime.Today.AddDays(1); // Start from tomorrow

        while (dates.Count < count)
        {
            if (cursor.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                dates.Add(cursor.ToString("dddd d MMMM yyyy"));

            cursor = cursor.AddDays(1);
        }

        return dates;
    }

    /// <summary>
    /// Returns the available time slots for a given date.
    /// MOCK: returns all standard slots regardless of date.
    /// </summary>
    public List<string> GetAvailableTimes()
        => TimeSlots.ToList();

    /// <summary>
    /// Confirms a booking and generates a reference number.
    /// MOCK: stores in-memory only. No real council system is called.
    /// READY FOR REAL API: replace body with HTTP call to council booking endpoint.
    /// </summary>
    public BookingConfirmation ConfirmBooking(AppointmentBookingData data)
    {
        var reference = GenerateReference();

        var confirmation = new BookingConfirmation
        {
            Reference       = reference,
            AppointmentType = data.AppointmentType,
            Date            = data.Date,
            Time            = data.Time,
            Success         = true,
            Message         = $"✅ Your appointment has been booked.\n\n" +
                              $"**Reference:** {reference}\n" +
                              $"**Type:** {data.AppointmentType}\n" +
                              $"**Date:** {data.Date}\n" +
                              $"**Time:** {data.Time}\n" +
                              $"**Name:** {data.Name}\n" +
                              (!string.IsNullOrWhiteSpace(data.Phone) ? $"**Phone:** {data.Phone}\n" : "") +
                              (!string.IsNullOrWhiteSpace(data.Email) ? $"**Email:** {data.Email}\n" : "") +
                              $"\nPlease bring proof of identity. To cancel or reschedule, " +
                              $"call 01274 431000 and quote your reference number."
        };

        _bookings[reference] = confirmation;
        return confirmation;
    }

    /// <summary>
    /// Looks up an existing booking by reference number.
    /// </summary>
    public BookingConfirmation? GetBooking(string reference)
        => _bookings.TryGetValue(reference.Trim().ToUpperInvariant(), out var b) ? b : null;

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static string GenerateReference()
    {
        var date   = DateTime.Today.ToString("yyyyMMdd");
        var suffix = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        return $"BCA-{date}-{suffix}";
    }
}

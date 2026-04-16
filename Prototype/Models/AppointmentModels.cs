namespace CouncilChatbotPrototype.Models;

/// <summary>
/// Represents a single bookable appointment slot.
/// MOCK — replace IsAvailable logic with a real availability API when available.
/// </summary>
public class AppointmentSlot
{
    public string Id              { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Date            { get; set; } = "";   // e.g. "Monday 14 April 2026"
    public string Time            { get; set; } = "";   // e.g. "10:00 AM"
    public string AppointmentType { get; set; } = "";
    public bool   IsAvailable     { get; set; } = true;
}

/// <summary>
/// Appointment types offered by Bradford Council. MOCK — swap for a real service catalogue.
/// </summary>
public class AppointmentTypeInfo
{
    public string Id              { get; set; } = "";
    public string Name            { get; set; } = "";
    public string Description     { get; set; } = "";
    public int    DurationMinutes { get; set; } = 30;
}

/// <summary>
/// Data collected during the in-chat appointment booking flow.
/// </summary>
public class AppointmentBookingData
{
    public string SessionId       { get; set; } = "";
    public string AppointmentType { get; set; } = "";
    public string Date            { get; set; } = "";
    public string Time            { get; set; } = "";
    public string Name            { get; set; } = "";
    public string Phone           { get; set; } = "";
    public string Email           { get; set; } = "";
    public string Notes           { get; set; } = "";
}

/// <summary>
/// Confirmation returned after a booking is created.
/// READY FOR REAL API: replace mock reference generation with a real booking system call.
/// </summary>
public class BookingConfirmation
{
    public string Reference       { get; set; } = "";   // e.g. "BCA-20260413-7F3A"
    public string AppointmentType { get; set; } = "";
    public string Date            { get; set; } = "";
    public string Time            { get; set; } = "";
    public string Message         { get; set; } = "";
    public bool   Success         { get; set; }
    public string Error           { get; set; } = "";
}

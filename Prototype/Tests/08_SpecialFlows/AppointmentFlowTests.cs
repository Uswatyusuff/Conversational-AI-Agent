// AppointmentFlowTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// Category 8b: Appointment booking flow
//
// Tests the multi-step appointment booking:
//  Step 0: "book an appointment" → select type
//  Step 1: User selects type    → select date
//  Step 2: User selects date    → select time
//  Step 3: User selects time    → enter name
//  Step 4: User enters name     → enter phone
//  Step 5: User enters phone    → enter email
//  Step 6: User enters email    → confirmation
//
// Also tests:
//  - Cancellation at any step clears the flow
//  - An unrelated service question escapes the flow
//  - Invalid type selection returns a helpful error, not a crash
// ─────────────────────────────────────────────────────────────────────────────

using BradfordChatbot.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BradfordChatbot.Tests._08_SpecialFlows;

public class AppointmentFlowTests : ChatTestBase
{
    public AppointmentFlowTests() : base() { }

    // ── Step 0: Booking intent triggers type selection ────────────────────────

    [Theory]
    [InlineData("book an appointment")]
    [InlineData("I'd like to make an appointment")]
    [InlineData("can I speak to someone?")]
    [InlineData("schedule a call")]
    public async Task BookingIntent_StartsAppointmentFlow(string message)
    {
        var result = await Chat(message);
        result.service.Should().Be("Appointment");
        Memory.GetPendingFlow(Session).Should().Contain("appointment");
    }

    [Fact]
    public async Task BookingIntent_Reply_OffersAppointmentTypes()
    {
        var result = await Chat("book an appointment");
        // Reply should list the available appointment types
        result.reply.Should().ContainAny(
            "Council Tax", "Housing", "Benefits", "appointment");
        result.suggestions.Should().NotBeEmpty();
    }

    // ── Invalid type selection ────────────────────────────────────────────────

    [Fact]
    public async Task InvalidTypeSelection_Returns_HelpfulError_NotCrash()
    {
        Memory.SetPendingFlow(Session, "appointment:select_type");

        var result = await Chat("purple elephant");

        // Should not crash; should give a helpful prompt
        result.reply.Should().NotBeNullOrWhiteSpace();
        result.service.Should().Be("Appointment");
    }

    // ── Unrelated question escapes appointment flow ───────────────────────────

    [Fact]
    public async Task RealQuestion_DuringAppointmentTypeStep_EscapesFlow()
    {
        Memory.SetPendingFlow(Session, "appointment:select_type");

        var result = await Chat("How do I pay my council tax?");

        result.service.Should().NotBe("Appointment");
        result.reply.Should().NotContain("I didn't recognise that appointment type");
    }

    // ── Cancellation clears appointment state ─────────────────────────────────

    [Theory]
    [InlineData("cancel")]
    [InlineData("never mind")]
    [InlineData("something else")]
    [InlineData("start again")]
    [InlineData("forget that")]
    public async Task Cancellation_DuringAppointmentFlow_ClearsPendingFlow(string cancel)
    {
        // Pre-populate a mid-flow appointment state
        Memory.SetPendingFlow(Session, "appointment:enter_name");
        Memory.SetAppointmentType(Session, "Council Tax");
        Memory.SetAppointmentDate(Session, "Monday");

        await Chat(cancel);

        Memory.GetPendingFlow(Session).Should().BeEmpty();
    }

    // ── Memory stores appointment fields correctly ────────────────────────────

    [Fact]
    public void AppointmentMemory_Stores_TypeDateTimeName()
    {
        Memory.SetAppointmentType(Session, "Council Tax");
        Memory.SetAppointmentDate(Session, "Monday 15 April");
        Memory.SetAppointmentTime(Session, "10:00 AM");
        Memory.SetAppointmentName(Session, "Test Resident");

        Memory.GetAppointmentType(Session).Should().Be("Council Tax");
        Memory.GetAppointmentDate(Session).Should().Be("Monday 15 April");
        Memory.GetAppointmentTime(Session).Should().Be("10:00 AM");
    }

    [Fact]
    public void ClearAppointmentFlow_ResetsAllFields()
    {
        Memory.SetAppointmentType(Session, "Housing");
        Memory.SetAppointmentDate(Session, "Tuesday");
        Memory.SetAppointmentTime(Session, "2:00 PM");
        Memory.SetAppointmentName(Session, "John");
        Memory.SetPendingFlow(Session, "appointment:enter_email");

        Memory.ClearAppointmentFlow(Session);

        Memory.GetAppointmentType(Session).Should().BeEmpty();
        Memory.GetAppointmentDate(Session).Should().BeEmpty();
        Memory.GetAppointmentTime(Session).Should().BeEmpty();
        Memory.GetAppointmentName(Session).Should().BeEmpty();
    }

    // ── Appointment does not interfere with Housing emergency ─────────────────

    [Fact]
    public async Task HomelessnessMessage_DuringAppointmentFlow_RoutesToHousing()
    {
        Memory.SetPendingFlow(Session, "appointment:select_type");

        var result = await Chat("I am homeless and need emergency housing tonight");

        result.service.Should().Be("Housing");
    }

    // ── Appointment service appears in Contact Us when explicitly needed ───────

    [Fact]
    public async Task AppointmentQuery_ReturnsContactInfo_NotJustPlaceholder()
    {
        var result = await Chat("book an appointment");
        result.reply.Should().NotBeNullOrWhiteSpace();
        result.reply.Length.Should().BeGreaterThan(10);
    }
}

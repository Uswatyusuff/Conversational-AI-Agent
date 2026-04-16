// ConversationMemoryTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// Category 2a: ConversationMemory unit tests
//
// These tests drive ConversationMemory directly (no HTTP, no embedding).
// ConversationMemory has zero external dependencies so real instances are used.
// ─────────────────────────────────────────────────────────────────────────────

using CouncilChatbotPrototype.Services;
using FluentAssertions;
using Xunit;

namespace BradfordChatbot.Tests._02_ContextMemory;

public class ConversationMemoryTests
{
    private readonly ConversationMemory _mem = new();
    private readonly string _s = $"session-{Guid.NewGuid():N}"; // unique per test

    // ── Basic get/set operations ───────────────────────────────────────────────

    [Fact]
    public void GetLastService_ReturnsEmpty_ForNewSession()
    {
        _mem.GetLastService(_s).Should().BeEmpty();
    }

    [Fact]
    public void SetLastService_Persists_AndCanBeRetrieved()
    {
        _mem.SetLastService(_s, "Council Tax");
        _mem.GetLastService(_s).Should().Be("Council Tax");
    }

    [Fact]
    public void SetLastService_Ignores_UnknownAndEmpty()
    {
        _mem.SetLastService(_s, "Council Tax");
        _mem.SetLastService(_s, "Unknown");  // should be ignored
        _mem.GetLastService(_s).Should().Be("Council Tax");

        _mem.SetLastService(_s, "");         // empty should also be ignored
        _mem.GetLastService(_s).Should().Be("Council Tax");
    }

    [Fact]
    public void SetLastService_Updates_WhenDifferentValidService()
    {
        _mem.SetLastService(_s, "Council Tax");
        _mem.SetLastService(_s, "Housing");
        _mem.GetLastService(_s).Should().Be("Housing");
    }

    [Fact]
    public void GetLastIntent_ReturnsEmpty_ForNewSession()
    {
        _mem.GetLastIntent(_s).Should().BeEmpty();
    }

    [Fact]
    public void SetAndGetLastIntent_RoundTrips_Correctly()
    {
        _mem.SetLastIntent(_s, "council_tax_payment");
        _mem.GetLastIntent(_s).Should().Be("council_tax_payment");
    }

    // ── Pending flow ──────────────────────────────────────────────────────────

    [Fact]
    public void PendingFlow_IsEmpty_ForNewSession()
    {
        _mem.GetPendingFlow(_s).Should().BeEmpty();
    }

    [Fact]
    public void SetPendingFlow_AndClear_WorksCorrectly()
    {
        _mem.SetPendingFlow(_s, "awaiting_postcode_for_bin_collection");
        _mem.GetPendingFlow(_s).Should().Be("awaiting_postcode_for_bin_collection");

        _mem.ClearPendingFlow(_s);
        _mem.GetPendingFlow(_s).Should().BeEmpty();
    }

    // ── Turn storage ──────────────────────────────────────────────────────────

    [Fact]
    public void AddTurn_And_GetRecentTurns_ReturnCorrectTurns()
    {
        _mem.AddTurn(_s, "user",      "How do I pay council tax?");
        _mem.AddTurn(_s, "assistant", "You can pay online or by direct debit.");

        var turns = _mem.GetRecentTurns(_s, 10);
        turns.Should().HaveCount(2);
        turns[0].Role.Should().Be("user");
        turns[1].Role.Should().Be("assistant");
    }

    [Fact]
    public void GetRecentTurns_Respects_TakeParameter()
    {
        for (var i = 0; i < 8; i++)
            _mem.AddTurn(_s, "user", $"Message {i}");

        var last3 = _mem.GetRecentTurns(_s, 3);
        last3.Should().HaveCount(3);
        last3[2].Message.Should().Contain("7");
    }

    [Fact]
    public void TurnsAreCappedAt10()
    {
        for (var i = 0; i < 15; i++)
            _mem.AddTurn(_s, "user", $"Message {i}");

        // GetRecentTurns(10) should return at most 10 turns
        var turns = _mem.GetRecentTurns(_s, 10);
        turns.Should().HaveCount(10);
    }

    [Fact]
    public void AddTurn_DoesNotStore_EmptyMessages()
    {
        _mem.AddTurn(_s, "user", "");
        _mem.AddTurn(_s, "user", "   ");
        _mem.GetRecentTurns(_s, 10).Should().BeEmpty();
    }

    // ── PII sanitisation in stored turns ─────────────────────────────────────

    [Fact]
    public void AddTurn_MasksPostcode_InStoredMessage()
    {
        _mem.AddTurn(_s, "user", "My postcode is BD3 8PX");
        var turn = _mem.GetRecentTurns(_s, 1).Single();
        turn.Message.Should().NotContain("BD3 8PX");
        turn.Message.Should().Contain("[POSTCODE]");
    }

    [Fact]
    public void AddTurn_MasksEmail_InStoredMessage()
    {
        _mem.AddTurn(_s, "user", "Email me at john@example.com");
        var turn = _mem.GetRecentTurns(_s, 1).Single();
        turn.Message.Should().NotContain("john@example.com");
        turn.Message.Should().Contain("[EMAIL]");
    }

    [Fact]
    public void AddTurn_MasksPhone_InStoredMessage()
    {
        _mem.AddTurn(_s, "user", "Call me on 01274 431000");
        var turn = _mem.GetRecentTurns(_s, 1).Single();
        turn.Message.Should().NotContain("01274 431000");
        turn.Message.Should().Contain("[PHONE]");
    }

    [Fact]
    public void AddTurn_MasksName_WhenExplicitlyGiven()
    {
        _mem.AddTurn(_s, "user", "My name is John Smith");
        var turn = _mem.GetRecentTurns(_s, 1).Single();
        turn.Message.Should().NotContain("John Smith");
    }

    // ── Address context ────────────────────────────────────────────────────────

    [Fact]
    public void AddressContext_StartsEmpty()
    {
        _mem.GetActivePostcode(_s).Should().BeEmpty();
        _mem.GetActiveAddress(_s).Should().BeEmpty();
        _mem.GetHasSelectedAddress(_s).Should().BeFalse();
    }

    [Fact]
    public void SetActivePostcode_And_Clear_WorksAtomically()
    {
        _mem.SetActivePostcode(_s, "BD1 1HY");
        _mem.SetActiveAddress(_s, "City Hall, Bradford");
        _mem.SetHasSelectedAddress(_s, true);
        _mem.SetLastBinResult(_s, "General waste: Monday");

        _mem.ClearAddressContext(_s);

        _mem.GetActivePostcode(_s).Should().BeEmpty();
        _mem.GetActiveAddress(_s).Should().BeEmpty();
        _mem.GetHasSelectedAddress(_s).Should().BeFalse();
        _mem.GetLastBinResult(_s).Should().BeEmpty();
    }

    [Fact]
    public void MaskPostcode_ProducesPartialMask()
    {
        _mem.SetMaskedPostcode(_s, "BD3 8PX");
        var masked = _mem.GetMaskedPostcode(_s);
        masked.Should().NotContain("BD3");
        masked.Should().Contain("8PX");
    }

    // ── Appointment flow state ────────────────────────────────────────────────

    [Fact]
    public void AppointmentFlow_CanStore_AndRetrieve_AllFields()
    {
        _mem.SetAppointmentType(_s, "Council Tax");
        _mem.SetAppointmentDate(_s, "Monday 15 April");
        _mem.SetAppointmentTime(_s, "10:00 AM");
        _mem.SetAppointmentName(_s, "Test User");
        _mem.SetAppointmentPhone(_s, "07700 900000");
        _mem.SetAppointmentEmail(_s, "test@example.com");

        _mem.GetAppointmentType(_s).Should().Be("Council Tax");
        _mem.GetAppointmentDate(_s).Should().Be("Monday 15 April");
        _mem.GetAppointmentTime(_s).Should().Be("10:00 AM");
    }

    [Fact]
    public void ClearAppointmentFlow_ResetsAllFields()
    {
        _mem.SetAppointmentType(_s, "Housing");
        _mem.SetAppointmentDate(_s, "Tuesday");
        _mem.ClearAppointmentFlow(_s);

        _mem.GetAppointmentType(_s).Should().BeEmpty();
        _mem.GetAppointmentDate(_s).Should().BeEmpty();
        _mem.GetAppointmentTime(_s).Should().BeEmpty();
    }

    // ── Session isolation ─────────────────────────────────────────────────────

    [Fact]
    public void DifferentSessions_DoNotShareState()
    {
        var s1 = $"iso-{Guid.NewGuid():N}";
        var s2 = $"iso-{Guid.NewGuid():N}";

        _mem.SetLastService(s1, "Council Tax");
        _mem.SetLastService(s2, "Housing");

        _mem.GetLastService(s1).Should().Be("Council Tax");
        _mem.GetLastService(s2).Should().Be("Housing");
    }
}

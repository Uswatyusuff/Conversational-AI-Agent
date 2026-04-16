// BinLookupFlowTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// Category 8a: Bin lookup flow
//
// Tests the multi-step bin collection flow:
//  1. User asks about bin collection → postcode requested
//  2. User supplies postcode → POSTCODE_LOOKUP:: signal emitted
//  3. User re-uses same address → shows cached result
//  4. User requests different address → clears and re-prompts
//  5. User cancels / asks unrelated question → exits flow cleanly
//
// The postcode-lookup step sends "POSTCODE_LOOKUP::BD3 8PX" as the reply,
// which is a special frontend signal. We test that the signal is present and
// correctly formatted.
// ─────────────────────────────────────────────────────────────────────────────

using BradfordChatbot.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BradfordChatbot.Tests._08_SpecialFlows;

public class BinLookupFlowTests : ChatTestBase
{
    // The LangChain stub for bin collection asks for postcode when none supplied
    public BinLookupFlowTests() : base(new LangChainStubResponse
    {
        Answer            = "Please enter your postcode so I can check your bin collection details.",
        Service           = "Waste & Bins",
        Action            = "clarify",
        NeedsClarification = true
    }) { }

    // ── Step 1: Bin collection intent prompts for postcode ────────────────────

    [Theory]
    [InlineData("When is my bin collection?")]
    [InlineData("What day are my bins collected?")]
    [InlineData("bin collection day")]
    public async Task BinCollectionQuery_Prompts_ForPostcode(string message)
    {
        var reply = await Reply(message);
        reply.Should().ContainAny("postcode", "Postcode");
    }

    // ── Step 2: Supplying a postcode emits POSTCODE_LOOKUP signal ─────────────

    [Fact]
    public async Task PostcodeSupplied_During_BinFlow_EmitsPostcodeLookupSignal()
    {
        // Prime the pending flow
        Memory.SetPendingFlow(Session, "awaiting_postcode_for_bin_collection");

        // Set LangChain to emit the POSTCODE_LOOKUP signal (as Python would)
        LangChainHandler.SetResponse("""
        {
            "answer": "POSTCODE_LOOKUP::BD3 8PX",
            "service": "Waste & Bins",
            "action": "tool",
            "needs_clarification": false,
            "tool_used": "postcode_lookup",
            "next_steps_url": ""
        }
        """);

        var result = await Chat("BD3 8PX");
        result.reply.Should().StartWith("POSTCODE_LOOKUP::");
    }

    [Fact]
    public async Task PostcodeLookupSignal_ContainsExactPostcode()
    {
        Memory.SetPendingFlow(Session, "awaiting_postcode_for_bin_collection");
        LangChainHandler.SetResponse("""
        {
            "answer": "POSTCODE_LOOKUP::BD3 8PX",
            "service": "Waste & Bins",
            "action": "tool",
            "needs_clarification": false,
            "tool_used": "postcode_lookup",
            "next_steps_url": ""
        }
        """);

        var result = await Chat("BD3 8PX");
        result.reply.Should().Contain("BD3 8PX");
    }

    [Fact]
    public async Task PostcodeLookupSignal_Format_IsExact()
    {
        // Verify the exact format: POSTCODE_LOOKUP::<postcode>
        Memory.SetPendingFlow(Session, "awaiting_postcode_for_bin_collection");
        LangChainHandler.SetResponse("""
        {
            "answer": "POSTCODE_LOOKUP::BD1 1HY",
            "service": "Waste & Bins",
            "action": "tool",
            "needs_clarification": false,
            "tool_used": "postcode_lookup",
            "next_steps_url": ""
        }
        """);

        var result = await Chat("BD1 1HY");
        // Must be exactly "POSTCODE_LOOKUP::" followed by the postcode — no spaces around ::
        result.reply.Should().MatchRegex(@"^POSTCODE_LOOKUP::[A-Z]{1,2}\d[A-Z\d]?\s?\d[A-Z]{2}$");
    }

    // ── Re-using same address ─────────────────────────────────────────────────

    [Fact]
    public async Task SameAddressIntent_WithCachedResult_ReturnsCachedBinResult()
    {
        // Pre-load a cached bin result
        Memory.SetHasSelectedAddress(Session, true);
        Memory.SetLastBinResult(Session, "General waste: Monday. Recycling: Tuesday.");
        Memory.SetLastService(Session, "Waste & Bins");

        var result = await Chat("same address");
        result.reply.Should().ContainAny("Monday", "Tuesday", "General waste", "Recycling");
    }

    // ── Different address ─────────────────────────────────────────────────────

    [Fact]
    public async Task DifferentAddressIntent_SetsPostcodeFlow_And_Prompts()
    {
        Memory.SetHasSelectedAddress(Session, true);
        Memory.SetLastService(Session, "Waste & Bins");

        var result = await Chat("use a different address");
        Memory.GetPendingFlow(Session).Should().Be("awaiting_postcode_for_bin_collection");
        result.reply.Should().ContainAny("postcode", "Postcode");
    }

    // ── Cancellation during bin flow ──────────────────────────────────────────

    [Theory]
    [InlineData("something else")]
    [InlineData("start again")]
    [InlineData("forget that")]
    public async Task CancellationDuringBinFlow_ClearsPendingFlow(string cancel)
    {
        Memory.SetPendingFlow(Session, "awaiting_postcode_for_bin_collection");

        await Chat(cancel);

        Memory.GetPendingFlow(Session).Should().BeEmpty();
    }

    // ── Unrelated question exits bin flow cleanly ─────────────────────────────

    [Fact]
    public async Task UnrelatedQuestion_DuringBinFlow_DoesNotAsk_ForPostcode()
    {
        Memory.SetPendingFlow(Session, "awaiting_postcode_for_bin_collection");

        // A totally different question about council tax should exit the bin flow
        var result = await Chat("How do I pay my council tax?");

        result.service.Should().NotBe("Waste & Bins");
        result.reply.Should().NotContain("postcode");
    }

    // ── Missed bin is handled directly (not via postcode flow) ────────────────

    [Fact]
    public async Task MissedBin_ReturnsReportInstructions_WithoutAskingPostcode()
    {
        var result = await Chat("my bin wasn't collected today");

        result.service.Should().Be("Waste & Bins");
        result.reply.Should().ContainAny("missed bin", "report", "fill in", "01274 431000");
        result.reply.Should().NotContain("postcode");
    }
}

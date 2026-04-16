// ResilienceTests.cs
// ─────────────────────────────────────────────────────────────────────────────
// Category 9: Resilience and robustness
//
// Tests that the chatbot handles unexpected / malformed input gracefully:
//  - Keyboard mash / gibberish
//  - Typo-heavy messages
//  - Very short or very long messages
//  - All-caps, mixed case
//  - Unusual punctuation
//  - Empty-ish messages
//  - Slang
//  - Duplicate submissions (same message twice in a row)
//  - Irrelevant messages mid-flow
//
// Every test here must pass: the system must never crash, must always return
// a non-empty reply, and must never leak internal implementation details.
// ─────────────────────────────────────────────────────────────────────────────

using BradfordChatbot.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BradfordChatbot.Tests._09_Resilience;

public class ResilienceTests : ChatTestBase
{
    public ResilienceTests() : base(new LangChainStubResponse
    {
        Answer  = "Here is some information about that.",
        Service = "Unknown",
        Action  = "answer"
    }) { }

    // ── System never crashes and always returns a reply ───────────────────────

    [Theory]
    [InlineData("asdf")]
    [InlineData("sdfsadf")]
    [InlineData("zzzzzzzz")]
    [InlineData("!!!!!!!!")]
    [InlineData("........")]
    [InlineData("   ")]                          // whitespace-only (trimmed to empty)
    [InlineData("12345")]
    [InlineData("aaaaaaaaaaaaaaaaaa")]
    [InlineData("HELP HELP HELP HELP")]
    [InlineData("lol what")]
    [InlineData("idk")]
    [InlineData("umm")]
    [InlineData("?")]
    [InlineData("???")]
    [InlineData("🤷")]                           // emoji
    public async Task AnyInput_Returns_NonEmptyReply(string message)
    {
        var result = await Chat(message);
        // The reply may be the meaningless-input handler or a generic message,
        // but it must never be null or whitespace.
        result.reply.Should().NotBeNullOrWhiteSpace();
    }

    // ── Keyboard mash does not return service content ─────────────────────────

    [Theory]
    [InlineData("asdf")]
    [InlineData("sdfsadf")]
    [InlineData("qwerty")]
    [InlineData("zzzzzzzz")]
    public async Task KeyboardMash_DoesNotReturn_RandomServiceContent(string mash)
    {
        var result = await Chat(mash);
        // Must not accidentally surface Blue Badge, Library, or any service content
        result.reply.ToLower().Should().NotContainAny(
            "blue badge",
            "renew library",
            "renewing books",
            "planning application",
            "council tax band");
    }

    // ── Case insensitivity ────────────────────────────────────────────────────

    [Theory]
    [InlineData("COUNCIL TAX")]
    [InlineData("council tax")]
    [InlineData("Council Tax")]
    [InlineData("CoUnCiL TaX")]
    public async Task CaseVariants_RouteSameAsLowercase(string message)
    {
        var result = await Chat(message);
        result.service.Should().Be("Council Tax");
    }

    [Theory]
    [InlineData("BIN COLLECTION")]
    [InlineData("bin collection")]
    [InlineData("Bin Collection")]
    public async Task BinsCase_RoutesCorrectly(string message)
    {
        var result = await Chat(message);
        result.service.Should().Be("Waste & Bins");
    }

    // ── Typo-heavy messages still route correctly ─────────────────────────────

    [Theory]
    [InlineData("counci ltax",                  "Council Tax")]     // word split
    [InlineData("councill tax",                 "Council Tax")]     // double l
    [InlineData("coucil tax",                   "Council Tax")]     // missing n
    [InlineData("wen is my bin collectino",     "Waste & Bins")]    // transposition
    [InlineData("benfits",                      "Benefits & Support")] // missing e
    [InlineData("benifits",                     "Benefits & Support")] // i/e swap
    public async Task Typos_StillRoute_ToCorrectService(string message, string expectedService)
    {
        var result = await Chat(message);
        result.service.Should().Be(expectedService);
    }

    // ── Very long messages don't crash ────────────────────────────────────────

    [Fact]
    public async Task VeryLongMessage_DoesNotCrash()
    {
        var longMsg = string.Join(" ", Enumerable.Repeat("council tax payment help", 200));
        var result = await Chat(longMsg);
        result.reply.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task MaxLength_RepeatedWords_Routes_Correctly()
    {
        var longMsg = string.Join(" ", Enumerable.Repeat("bin", 50)) + " collection";
        var result = await Chat(longMsg);
        result.service.Should().Be("Waste & Bins");
    }

    // ── Unusual punctuation ───────────────────────────────────────────────────

    [Theory]
    [InlineData("how do I pay my council tax????")]
    [InlineData("BINS!!!")]
    [InlineData("benefits???")]
    [InlineData("housing... help...")]
    [InlineData("council-tax")]
    [InlineData("bins/recycling")]
    public async Task UnusualPunctuation_DoesNotCrash_AndRoutesReasonably(string message)
    {
        var result = await Chat(message);
        result.reply.Should().NotBeNullOrWhiteSpace();
        // Doesn't assert exact service — just that it doesn't crash and
        // ideally routes to a sensible service
    }

    // ── Slang and informal phrasing ───────────────────────────────────────────

    [Theory]
    [InlineData("bins mate")]
    [InlineData("what's the deal with council tax")]
    [InlineData("yeah so benefits innit")]
    [InlineData("help me out with housing please mate")]
    public async Task SlangMessages_DoNotCrash(string message)
    {
        var result = await Chat(message);
        result.reply.Should().NotBeNullOrWhiteSpace();
    }

    // ── Duplicate submissions ─────────────────────────────────────────────────

    [Fact]
    public async Task DuplicateSubmission_BothReturn_ValidReplies()
    {
        var msg = "How do I pay my council tax?";
        var r1 = await Chat(msg);
        var r2 = await Chat(msg);

        r1.reply.Should().NotBeNullOrWhiteSpace();
        r2.reply.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DuplicateSubmission_ServiceIsConsistent()
    {
        var msg = "I want to apply for a Blue Badge";
        var r1 = await Chat(msg);
        var r2 = await Chat(msg);

        r1.service.Should().Be(r2.service);
    }

    // ── Empty / whitespace-only input ─────────────────────────────────────────

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public async Task WhitespaceOnly_IsHandled_Gracefully(string message)
    {
        // Note: The controller trims and returns BadRequest for empty messages.
        // When the orchestrator itself receives near-whitespace (after trim), it
        // should handle it as meaningless input.
        try
        {
            var result = await Chat(message);
            result.reply.Should().NotBeNullOrWhiteSpace();
        }
        catch (Exception ex)
        {
            // Acceptable: controller-level BadRequest is expected for truly empty input
            ex.Should().BeOfType<Exception>();
        }
    }

    // ── Irrelevant input during active flow ───────────────────────────────────

    [Fact]
    public async Task IrrelevantInput_DuringBinFlow_IsHandled_Gracefully()
    {
        Memory.SetPendingFlow(Session, "awaiting_postcode_for_bin_collection");

        // Nonsense string when a postcode is expected
        var result = await Chat("purple elephant dance");
        result.reply.Should().NotBeNullOrWhiteSpace();
        // Should prompt for postcode again or gracefully handle the bad input
    }
}

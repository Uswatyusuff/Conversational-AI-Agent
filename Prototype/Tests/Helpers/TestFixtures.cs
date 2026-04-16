// TestFixtures.cs
// ─────────────────────────────────────────────────────────────────────────────
// Static test data: sample FaqChunks, session IDs, and common message sets
// used across multiple test files.
//
// Vector design
// ─────────────
// We use 8-dimensional orthogonal-ish vectors so that each service cluster
// has a "home" dimension that scores highest for queries from that service.
// This gives the RetrievalService deterministic behaviour without needing a
// real embedding model. Values are chosen to be clearly distinct:
//   Council Tax  → [1,0,0,0,0,0,0,0]
//   Waste & Bins → [0,1,0,0,0,0,0,0]
//   Benefits     → [0,0,1,0,0,0,0,0]
//   Education    → [0,0,0,1,0,0,0,0]
//   Housing      → [0,0,0,0,1,0,0,0]
//   Planning     → [0,0,0,0,0,1,0,0]
//   Libraries    → [0,0,0,0,0,0,1,0]
//   Contact Us   → [0,0,0,0,0,0,0,1]
//
// For retrieval tests pass the appropriate query vector for the service.
// ─────────────────────────────────────────────────────────────────────────────

using CouncilChatbotPrototype.Models;

namespace BradfordChatbot.Tests.Helpers;

public static class TestFixtures
{
    // ── Per-service "home" query vectors for retrieval tests ──────────────────
    public static float[] CouncilTaxVector  => new[] { 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
    public static float[] WasteBinsVector   => new[] { 0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f };
    public static float[] BenefitsVector    => new[] { 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f };
    public static float[] EducationVector   => new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f };
    public static float[] HousingVector     => new[] { 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f };
    public static float[] PlanningVector    => new[] { 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f };
    public static float[] LibrariesVector   => new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f };
    public static float[] ContactUsVector   => new[] { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f };

    // ── Sample FAQ chunks pre-loaded into RetrievalService for tests ──────────
    public static IReadOnlyList<FaqChunk> SampleChunks { get; } = new List<FaqChunk>
    {
        new FaqChunk
        {
            Id           = "ctax_pay",
            Service      = "Council Tax",
            Title        = "How to pay your Council Tax",
            Text         = "You can pay your Council Tax online, by direct debit, at a PayPoint outlet, or by phone on 01274 431000. Direct debit is the easiest method and can be set up via the council website.",
            NextStepsUrl = "https://www.bradford.gov.uk/council-tax/pay-your-council-tax/",
            Vector       = new[] { 0.95f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f }
        },
        new FaqChunk
        {
            Id           = "ctax_discount",
            Service      = "Council Tax",
            Title        = "Council Tax discounts and exemptions",
            Text         = "You may be eligible for a 25% discount if you live alone. Full-time students are exempt. People with severe mental impairment may also qualify for an exemption.",
            NextStepsUrl = "https://www.bradford.gov.uk/council-tax/discounts-and-exemptions/",
            Vector       = new[] { 0.92f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f }
        },
        new FaqChunk
        {
            Id           = "ctax_support",
            Service      = "Benefits & Support",
            Title        = "Council Tax Support",
            Text         = "Council Tax Support (also called Council Tax Reduction) helps people on low incomes pay their council tax bill. You can apply online or by contacting the council.",
            NextStepsUrl = "https://www.bradford.gov.uk/benefits/council-tax-support/",
            Vector       = new[] { 0.30f, 0.01f, 0.88f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f }
        },
        new FaqChunk
        {
            Id           = "bins_collection",
            Service      = "Waste & Bins",
            Title        = "Check your bin collection dates",
            Text         = "Enter your postcode to find out when your bins are collected. Bins are collected fortnightly. General waste is collected on alternating weeks to recycling.",
            NextStepsUrl = "https://www.bradford.gov.uk/waste-and-recycling/check-your-bin-collection-dates/",
            Vector       = new[] { 0.01f, 0.95f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f }
        },
        new FaqChunk
        {
            Id           = "bins_missed",
            Service      = "Waste & Bins",
            Title        = "Report a missed bin collection",
            Text         = "If your bin was not collected on the scheduled day, report it using the missed bin form on the Bradford Council website or call 01274 431000.",
            NextStepsUrl = "https://www.bradford.gov.uk/waste-and-recycling/missed-bin-collections/",
            Vector       = new[] { 0.01f, 0.93f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f }
        },
        new FaqChunk
        {
            Id           = "benefits_blue_badge",
            Service      = "Benefits & Support",
            Title        = "Blue Badge scheme",
            Text         = "The Blue Badge scheme provides parking concessions for disabled people. You may be eligible if you receive higher rate DLA mobility, PIP (8+ points for moving around), or have a severe walking difficulty.",
            NextStepsUrl = "https://www.bradford.gov.uk/benefits/blue-badge-scheme/",
            Vector       = new[] { 0.01f, 0.01f, 0.93f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f }
        },
        new FaqChunk
        {
            Id           = "benefits_fsm",
            Service      = "Benefits & Support",
            Title        = "Free school meals",
            Text         = "Your child may qualify for free school meals if you receive Universal Credit, Income Support, or similar qualifying benefits. Apply online via the Bradford Council website.",
            NextStepsUrl = "https://www.bradford.gov.uk/education-and-learning/free-school-meals/",
            Vector       = new[] { 0.01f, 0.01f, 0.90f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f }
        },
        new FaqChunk
        {
            Id           = "education_admissions",
            Service      = "Education",
            Title        = "School admissions",
            Text         = "Apply for a school place online during the admissions round. Primary school applications for September entry close in January. Secondary school applications close in October.",
            NextStepsUrl = "https://www.bradford.gov.uk/education-and-learning/school-admissions/",
            Vector       = new[] { 0.01f, 0.01f, 0.01f, 0.94f, 0.01f, 0.01f, 0.01f, 0.01f }
        },
        new FaqChunk
        {
            Id           = "housing_homeless",
            Service      = "Housing",
            Title        = "Homelessness and housing crisis",
            Text         = "If you are homeless or at risk of becoming homeless, contact the Housing Options team immediately on 01274 431000. They can assess your situation and explore housing options including emergency accommodation.",
            NextStepsUrl = "https://www.bradford.gov.uk/housing/homelessness/",
            Vector       = new[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.94f, 0.01f, 0.01f, 0.01f }
        },
        new FaqChunk
        {
            Id           = "planning_applications",
            Service      = "Planning",
            Title        = "View and comment on planning applications",
            Text         = "You can search for and comment on planning applications using the Bradford Council planning portal. Search by reference number, address, or postcode.",
            NextStepsUrl = "https://www.bradford.gov.uk/planning-and-building-control/view-planning-applications/",
            Vector       = new[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.94f, 0.01f, 0.01f }
        },
        new FaqChunk
        {
            Id           = "libraries_renew",
            Service      = "Libraries",
            Title        = "Renew library books",
            Text         = "You can renew library books online, by phone on 01535 618010, or in person at any Bradford library. Items can be renewed up to three times if there is no reservation.",
            NextStepsUrl = "https://www.bradford.gov.uk/libraries/renewing-borrowing-and-reserving-items/",
            Vector       = new[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.93f, 0.01f }
        },
        new FaqChunk
        {
            Id           = "contact_us",
            Service      = "Contact Us",
            Title        = "Contact Bradford Council",
            Text         = "Phone: 01274 431000 (Mon-Thu 8:30am-5pm, Fri 8:30am-4:30pm). Website: bradford.gov.uk. City Hall, Centenary Square, Bradford, BD1 1HY.",
            NextStepsUrl = "https://www.bradford.gov.uk/contact-us/",
            Vector       = new[] { 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.01f, 0.93f }
        },
    };

    // ── Session ID helpers ────────────────────────────────────────────────────

    // Generate a unique session ID per test to avoid state leakage
    public static string NewSession() => $"test-{Guid.NewGuid():N}";

    // ── Common message sets ────────────────────────────────────────────────────

    public static IEnumerable<string> GreetingMessages => new[]
    {
        "hi", "hello", "hey", "good morning", "good afternoon", "good evening",
        "hi there", "hello there", "howdy"
    };

    public static IEnumerable<string> FarewellMessages => new[]
    {
        "bye", "goodbye", "see you", "thanks bye", "cheers", "thanks", "thank you"
    };

    public static IEnumerable<string> MeaninglessMessages => new[]
    {
        "asdf", "sdfsadf", "zzzzz", "qwerty", "aaaaaa", "...", "???",
        "idk", "i dont know", "not sure"
    };

    public static IEnumerable<string> NameIntroductionMessages => new[]
    {
        "I am John",
        "I am Sarah",
        "my name is Ahmed",
        "I am Samik",
        "I'm David",
        "I am James Wilson"
    };

    public static IEnumerable<string> VagueHelpMessages => new[]
    {
        "help",
        "I need help",
        "can you help me",
        "I want some help",
        "help me please"
    };

    // Messages that contain "I am" but are NOT name introductions
    public static IEnumerable<string> NotNameIntroductionMessages => new[]
    {
        "I am at risk of eviction",
        "I am homeless",
        "I am struggling to pay my council tax",
        "I am eligible for a blue badge",
        "I am worried about my benefits",
        "I am applying for a school place",
        "I am currently receiving universal credit"
    };

    // ── Banned phrases in responses ───────────────────────────────────────────

    public static IEnumerable<string> BannedResponsePhrases => new[]
    {
        "context does not provide",
        "context does not specify",
        "context does not clearly",
        "the context does not",
        "not specified in the context",
        "not mentioned in the context",
        "not available in the provided context",
        "i am not able to find",
        "i'm not able to find",
        "i cannot find",
        "i can't find",
        "i don't have specific information",
        "i do not have specific information",
        "based on the context provided",
        "according to the context",
        "the context says",
        "based on the context"
    };
}

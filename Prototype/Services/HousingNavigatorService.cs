namespace CouncilChatbotPrototype.Services;

/// <summary>
/// Decision-tree style housing navigator.
///
/// Handles urgent housing situations (rough sleeping, eviction risk, domestic abuse)
/// with prioritised, step-by-step conversation paths.
///
/// All responses are integrated into the normal chat flow via ChatOrchestrator —
/// no separate UI component is required.
/// </summary>
public class HousingNavigatorService
{
    // ── Decision tree node identifiers ───────────────────────────────────────────
    public const string NodeRoughSleeping      = "housing:rough_sleeping";
    public const string NodeEvictionRisk       = "housing:eviction_risk";
    public const string NodeDomesticAbuse      = "housing:domestic_abuse";
    public const string NodeTemporaryAccomm    = "housing:temp_accommodation";
    public const string NodeAffordability      = "housing:affordability";
    public const string NodeRepairs            = "housing:repairs";
    public const string NodeFindHome           = "housing:find_home";
    public const string NodeGeneral            = "housing:general";

    /// <summary>
    /// Analyses the user's message and picks the most appropriate housing node.
    /// Returns null if the message is not housing-related enough for a specialised path.
    /// </summary>
    public string? DetectHousingNode(string normMsg)
    {
        if (string.IsNullOrWhiteSpace(normMsg)) return null;

        // ── Rough sleeping / currently homeless ─────────────────────────────────
        if (normMsg.Contains("rough sleeping") ||
            normMsg.Contains("sleeping rough") ||
            normMsg.Contains("no where to sleep") ||
            normMsg.Contains("nowhere to sleep") ||
            normMsg.Contains("no place to stay") ||
            normMsg.Contains("outside tonight") ||
            normMsg.Contains("im homeless") ||
            normMsg.Contains("i am homeless") ||
            normMsg.Contains("homeless tonight") ||
            normMsg.Contains("street") && normMsg.Contains("homeless"))
            return NodeRoughSleeping;

        // ── Eviction / at risk ────────────────────────────────────────────────────
        if (normMsg.Contains("eviction") ||
            normMsg.Contains("being evicted") ||
            normMsg.Contains("evicted") ||
            normMsg.Contains("section 21") ||
            normMsg.Contains("section 8") ||
            normMsg.Contains("notice to leave") ||
            normMsg.Contains("notice to quit") ||
            normMsg.Contains("landlord wants me out") ||
            normMsg.Contains("lose my home") ||
            normMsg.Contains("losing my home") ||
            normMsg.Contains("at risk of losing"))
            return NodeEvictionRisk;

        // ── Domestic abuse ────────────────────────────────────────────────────────
        if (normMsg.Contains("domestic abuse") ||
            normMsg.Contains("domestic violence") ||
            normMsg.Contains("fleeing violence") ||
            normMsg.Contains("unsafe at home") ||
            normMsg.Contains("not safe at home"))
            return NodeDomesticAbuse;

        // ── Temporary accommodation ───────────────────────────────────────────────
        if (normMsg.Contains("temporary accommodation") ||
            normMsg.Contains("emergency accommodation") ||
            normMsg.Contains("hostel") ||
            normMsg.Contains("night shelter"))
            return NodeTemporaryAccomm;

        // ── Affordability ─────────────────────────────────────────────────────────
        if (normMsg.Contains("cant afford rent") ||
            normMsg.Contains("can't afford rent") ||
            normMsg.Contains("behind on rent") ||
            normMsg.Contains("rent arrears") ||
            normMsg.Contains("struggling to pay rent"))
            return NodeAffordability;

        // ── Repairs ───────────────────────────────────────────────────────────────
        if (normMsg.Contains("repair") ||
            normMsg.Contains("fix") && normMsg.Contains("house") ||
            normMsg.Contains("damp") ||
            normMsg.Contains("mould") ||
            normMsg.Contains("broken") && (normMsg.Contains("boiler") || normMsg.Contains("window") || normMsg.Contains("door")))
            return NodeRepairs;

        // ── Finding a home ────────────────────────────────────────────────────────
        if (normMsg.Contains("find a home") ||
            normMsg.Contains("need a home") ||
            normMsg.Contains("looking for housing") ||
            normMsg.Contains("council house") ||
            normMsg.Contains("social housing") ||
            normMsg.Contains("housing list") ||
            normMsg.Contains("housing register") ||
            normMsg.Contains("waiting list"))
            return NodeFindHome;

        return NodeGeneral;
    }

    /// <summary>
    /// Returns the appropriate response and suggestion chips for a housing node.
    /// </summary>
    public (string reply, List<string> suggestions, string nextStepsUrl) GetNodeResponse(string node)
    {
        return node switch
        {
            NodeRoughSleeping => (
                "⚠️ **This is urgent.** If you have nowhere to sleep tonight, please contact Bradford Council's Emergency Housing Team immediately:\n\n" +
                "📞 **Emergency Homeless Line: 01274 435999** (24 hours, 7 days a week)\n\n" +
                "If you need help during office hours, you can also visit:\n" +
                "📍 **Argus Chambers, 1 Filey Street, Bradford, BD1 5NL**\n\n" +
                "You may also be able to refer yourself to the Street Population Outreach Team (SPOT) if you know someone sleeping rough:\n" +
                "🔗 Streetlink: https://www.streetlink.org.uk/\n\n" +
                "Would you like help with what to expect when you make the call?",
                new List<string> { "What happens when I call?", "What do I need to bring?", "Housing advice", "Benefits & Support" },
                "https://www.bradford.gov.uk/housing/homelessness/how-to-get-help-if-you-are-homeless/"
            ),

            NodeEvictionRisk => (
                "If you have received a notice to leave or are at risk of eviction, **don't wait** — the sooner you act, the more options you have.\n\n" +
                "**Immediate steps:**\n" +
                "1️⃣ Read your notice carefully — check the date and type (Section 21 or Section 8)\n" +
                "2️⃣ Contact Bradford Council's Housing Options Team: 📞 **01274 435999**\n" +
                "3️⃣ Get free legal advice from Citizens Advice: 📞 **0800 144 8848**\n\n" +
                "**Important:** Your landlord must follow the correct legal process. You do not have to leave immediately when you receive a notice.\n\n" +
                "Are you currently in private rented accommodation or council/social housing?",
                new List<string> { "I rent privately", "I'm in council housing", "I need emergency housing", "Benefits & Support" },
                "https://www.bradford.gov.uk/housing/homelessness/how-to-get-help-if-you-are-homeless/"
            ),

            NodeDomesticAbuse => (
                "Your safety is the most important thing. If you are in immediate danger, **call 999**.\n\n" +
                "For housing support as a result of domestic abuse:\n\n" +
                "📞 **Bradford Domestic Violence Service (BDVS): 01274 730 930**\n" +
                "📞 **National Domestic Abuse Helpline: 0808 2000 247** (free, 24 hours)\n\n" +
                "Bradford Council can provide **emergency accommodation** and a referral to specialist support services. " +
                "You do not need to have proof of abuse to ask for help.\n\n" +
                "Would you like more information about emergency housing options?",
                new List<string> { "Emergency housing", "What support is available?", "Housing advice", "Benefits & Support" },
                "https://www.bradford.gov.uk/housing/homelessness/how-to-get-help-if-you-are-homeless/"
            ),

            NodeTemporaryAccomm => (
                "Bradford Council can provide emergency and temporary accommodation if you have nowhere to stay.\n\n" +
                "To request temporary accommodation:\n" +
                "📞 Call the Housing Options Team: **01274 435999**\n" +
                "🕐 Office hours: Mon–Thu 8:30 AM – 5:00 PM, Fri 8:30 AM – 4:30 PM\n" +
                "🚨 Out of hours emergencies: **01274 435999** (24-hour line)\n\n" +
                "You will be asked to explain your housing situation and any vulnerabilities. " +
                "The council has a duty to help if you are eligible under the Housing Act 1996.\n\n" +
                "Do you need help understanding what 'priority need' means for temporary accommodation?",
                new List<string> { "What is priority need?", "What happens at the housing assessment?", "Emergency housing", "Benefits & Support" },
                "https://www.bradford.gov.uk/housing/homelessness/how-to-get-help-if-you-are-homeless/"
            ),

            NodeAffordability => (
                "If you are struggling to pay your rent, there are several forms of help available:\n\n" +
                "💰 **Housing Benefit / Local Housing Allowance** — may cover part or all of your rent if you're on a low income\n" +
                "💰 **Universal Credit housing element** — included in your UC payment if eligible\n" +
                "💰 **Discretionary Housing Payment (DHP)** — extra help if your benefit doesn't cover full rent\n\n" +
                "To apply, contact Bradford Council Benefits team or visit:\n" +
                "🔗 https://www.bradford.gov.uk/benefits/applying-for-benefits/housing-benefit-and-council-tax-reduction/\n\n" +
                "Would you like to check what benefits you might be eligible for?",
                new List<string> { "Apply for Housing Benefit", "What is a Discretionary Housing Payment?", "Universal Credit help", "Benefits & Support" },
                "https://www.bradford.gov.uk/benefits/applying-for-benefits/housing-benefit-and-council-tax-reduction/"
            ),

            NodeRepairs => (
                "If you need repairs to a council-owned property, you have the right to have them fixed within a reasonable time.\n\n" +
                "**How to report a repair:**\n" +
                "📞 Phone: **01274 527777** (Repairs Line)\n" +
                "🌐 Online: https://www.bradford.gov.uk/housing/advice-for-tenants/getting-repairs-done/\n\n" +
                "**Emergency repairs** (e.g. no heating, flooding, unsafe structure):\n" +
                "📞 **01274 527777** — available 24 hours for emergencies\n\n" +
                "For private tenants, your landlord is legally responsible for most repairs. If your landlord won't act, contact Environmental Health on **01274 431000**.\n\n" +
                "What type of repair do you need help with?",
                new List<string> { "Report an emergency repair", "My landlord won't fix repairs", "Damp and mould", "Housing advice" },
                "https://www.bradford.gov.uk/housing/advice-for-tenants/getting-repairs-done/"
            ),

            NodeFindHome => (
                "To apply for a council or social housing property in Bradford, you need to join the **Housing Register**.\n\n" +
                "**Steps to apply:**\n" +
                "1️⃣ Check if you are eligible (you must live in the Bradford district or have a local connection)\n" +
                "2️⃣ Register online at: 🔗 https://www.bradford.gov.uk/housing/finding-a-home/how-can-i-find-a-home/\n" +
                "3️⃣ Provide proof of identity, address, and income\n" +
                "4️⃣ You will be placed in a priority band based on your housing need\n\n" +
                "**Average waiting times** vary considerably depending on the area and property size needed.\n\n" +
                "Would you like help filling in a housing application? I can guide you through it step by step.",
                new List<string> { "Start housing application", "Check eligibility", "What is priority banding?", "Housing Benefit help" },
                "https://www.bradford.gov.uk/housing/finding-a-home/how-can-i-find-a-home/"
            ),

            _ => (
                "Bradford Council can help with a wide range of housing issues.\n\n" +
                "Common housing services:\n" +
                "• **Finding a home** — join the Housing Register\n" +
                "• **Homelessness** — emergency help if you have nowhere to go\n" +
                "• **Repairs** — report repairs to your council property\n" +
                "• **Eviction** — get advice if you're at risk of losing your home\n" +
                "• **Affordability** — help with rent payments and benefits\n\n" +
                "What would you like help with?",
                new List<string> { "I am homeless", "I'm at risk of eviction", "I need housing repairs", "How do I find a home?" },
                "https://www.bradford.gov.uk/housing/housing/"
            )
        };
    }
}

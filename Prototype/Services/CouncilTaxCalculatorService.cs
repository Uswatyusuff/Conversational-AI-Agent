namespace CouncilChatbotPrototype.Services;

/// <summary>
/// Handles Council Tax payment planning, arrears guidance, and affordability conversations.
///
/// The calculator flow is fully chat-driven — no separate page is needed.
/// State is stored as a PendingFlow string in ConversationMemory:
///   "ctax_calc:awaiting_bill"   → waiting for the user's monthly bill amount
///   "ctax_calc:awaiting_missed" → waiting for number of missed months
///   "ctax_calc:done"            → calculation complete
/// </summary>
public class CouncilTaxCalculatorService
{
    // ── Flow state constants (used in ConversationMemory.PendingFlow) ────────────
    public const string FlowAwaitingBill   = "ctax_calc:awaiting_bill";
    public const string FlowAwaitingMissed = "ctax_calc:awaiting_missed";

    // Bradford Council Tax bands 2025/26 (approximate — MOCK, update each year)
    private static readonly Dictionary<string, decimal> BandAmounts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = 1_234.56m,
        ["B"] = 1_440.32m,
        ["C"] = 1_646.08m,
        ["D"] = 1_851.84m,
        ["E"] = 2_263.36m,
        ["F"] = 2_674.88m,
        ["G"] = 3_086.40m,
        ["H"] = 3_703.68m,
    };

    // ─────────────────────────────────────────────────────────────────────────────
    // Intent detection
    // ─────────────────────────────────────────────────────────────────────────────

    public bool IsCalculatorIntent(string normMsg)
    {
        if (string.IsNullOrWhiteSpace(normMsg)) return false;

        return normMsg.Contains("payment plan") ||
               normMsg.Contains("payment planner") ||
               normMsg.Contains("calculate") && normMsg.Contains("council tax") ||
               normMsg.Contains("how much do i owe") ||
               normMsg.Contains("cant afford") && normMsg.Contains("council tax") ||
               normMsg.Contains("can't afford") && normMsg.Contains("council tax") ||
               normMsg.Contains("missed payment") && normMsg.Contains("council tax") ||
               normMsg.Contains("council tax arrears") ||
               normMsg.Contains("behind on council tax") ||
               normMsg.Contains("arrears plan") ||
               normMsg.Contains("spreading my council tax") ||
               normMsg.Contains("spread the cost") && normMsg.Contains("council tax");
    }

    public bool IsArrearsIntent(string normMsg)
    {
        if (string.IsNullOrWhiteSpace(normMsg)) return false;

        return normMsg.Contains("arrears") ||
               normMsg.Contains("owe council tax") ||
               normMsg.Contains("fallen behind") ||
               normMsg.Contains("behind with council tax") ||
               normMsg.Contains("enforcement") ||
               normMsg.Contains("bailiff") && normMsg.Contains("council tax") ||
               normMsg.Contains("summons") && normMsg.Contains("council tax") ||
               normMsg.Contains("liability order");
    }

    public bool IsRemindersIntent(string normMsg)
    {
        if (string.IsNullOrWhiteSpace(normMsg)) return false;

        return normMsg.Contains("reminder") && normMsg.Contains("council tax") ||
               normMsg.Contains("notification") && normMsg.Contains("council tax") ||
               normMsg.Contains("remind me") && normMsg.Contains("council tax");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Response generation
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the payment calculator flow. Returns the first question.
    /// </summary>
    public (string reply, List<string> suggestions) StartCalculatorFlow()
    {
        var reply =
            "I can help you work out a Council Tax payment plan.\n\n" +
            "First: **what is your monthly Council Tax bill?** (You can find this on your bill or at MyInfo.)\n\n" +
            "If you don't know, you can also tell me your Council Tax band (A–H) and I'll estimate it for you.";

        var suggestions = new List<string> { "Band A", "Band B", "Band C", "Band D", "I don't know my band" };

        return (reply, suggestions);
    }

    /// <summary>
    /// Processes the user's bill amount input and asks how many months they've missed.
    /// Returns null if the input can't be parsed as a number or band.
    /// </summary>
    public (string reply, List<string> suggestions, decimal? parsedAmount) ProcessBillInput(string input)
    {
        input = input?.Trim() ?? "";

        // Try band lookup (e.g. "Band D" or just "D")
        var bandKey = ExtractBandLetter(input);
        if (bandKey != null && BandAmounts.TryGetValue(bandKey, out var annual))
        {
            var monthly = Math.Round(annual / 10m, 2); // Council Tax is usually 10 monthly instalments
            var reply =
                $"Band {bandKey.ToUpperInvariant()} in Bradford is approximately **£{annual:N2}/year**, " +
                $"which is about **£{monthly:N2}/month** over 10 instalments.\n\n" +
                $"How many months of Council Tax have you missed or are you struggling to pay?";
            return (reply, new List<string> { "1 month", "2 months", "3 months", "More than 3" }, monthly);
        }

        // Try parsing as a currency amount
        var cleaned = input.Replace("£", "").Replace(",", "").Trim();
        if (decimal.TryParse(cleaned, out var amount) && amount > 0)
        {
            var reply =
                $"Got it — your monthly bill is **£{amount:N2}**.\n\n" +
                $"How many months have you missed or are you behind?";
            return (reply, new List<string> { "1 month", "2 months", "3 months", "More than 3" }, amount);
        }

        // Unparseable
        return (
            "I didn't quite catch that. Please enter your monthly Council Tax bill as a number (e.g. **£185**), " +
            "or tell me your band (A, B, C, D, E, F, G, or H).",
            new List<string> { "Band A", "Band B", "Band C", "Band D", "I don't know" },
            null
        );
    }

    /// <summary>
    /// Generates a full payment breakdown given the monthly bill and number of missed months.
    /// </summary>
    public (string reply, List<string> suggestions) GeneratePaymentPlan(decimal monthlyBill, string missedInput)
    {
        int missedMonths = ParseMissedMonths(missedInput);
        if (missedMonths <= 0) missedMonths = 1;

        var arrears = Math.Round(monthlyBill * missedMonths, 2);

        // Suggest a catch-up plan over 3 or 6 months
        var catchUp3  = Math.Round(arrears / 3m + monthlyBill, 2);
        var catchUp6  = Math.Round(arrears / 6m + monthlyBill, 2);

        var reply =
            $"**Your Council Tax Payment Summary**\n\n" +
            $"Monthly bill:          £{monthlyBill:N2}\n" +
            $"Months missed:         {missedMonths}\n" +
            $"Total arrears:         £{arrears:N2}\n\n" +
            $"**Catch-up payment options:**\n" +
            $"• Pay arrears over 3 months: **£{catchUp3:N2}/month**\n" +
            $"• Pay arrears over 6 months: **£{catchUp6:N2}/month**\n\n" +
            $"**Recommended next steps:**\n" +
            $"1️⃣ Contact Bradford Council as soon as possible on **01274 431000** — it's much easier to arrange a plan before a summons is issued\n" +
            $"2️⃣ Check if you qualify for **Council Tax Support** to reduce your bill\n" +
            $"3️⃣ Ask about a **Direct Debit** for easier monthly management\n\n" +
            $"Would you like help with Council Tax Support eligibility or setting up a Direct Debit?";

        return (reply, new List<string> { "Apply for Council Tax Support", "Set up Direct Debit", "Speak to someone about arrears", "Council Tax discounts" });
    }

    /// <summary>
    /// Returns arrears guidance — for when the user is behind and may have received a reminder notice.
    /// </summary>
    public (string reply, List<string> suggestions) GetArrearsGuidance()
    {
        var reply =
            "If you are behind on Council Tax, here is what you need to know:\n\n" +
            "**The enforcement stages are:**\n" +
            "1. **Reminder notice** — you have 7 days to pay\n" +
            "2. **Final notice** — you have lost your right to pay by instalments; full year's balance is due\n" +
            "3. **Court summons** — Bradford Council applies to the Magistrates' Court for a Liability Order\n" +
            "4. **Liability Order** — additional court costs (typically £75+) are added to your debt\n" +
            "5. **Enforcement agents (bailiffs)** — may be instructed to collect the debt\n\n" +
            "⚠️ **Contact the Council before Stage 3.** Once a Liability Order is issued, costs increase significantly.\n\n" +
            "📞 Bradford Council Tax team: **01274 431000**\n" +
            "💡 You may also qualify for **Council Tax Support** which could reduce your bill going forward.\n\n" +
            "Would you like to use the payment calculator to work out a repayment plan?";

        return (reply, new List<string> { "Calculate my arrears", "Apply for Council Tax Support", "Set up a payment plan", "What is a Liability Order?" });
    }

    /// <summary>
    /// FUTURE INTEGRATION POINT: Returns reminders guidance.
    /// Real reminder functionality would integrate with the user's MyBradford account.
    /// </summary>
    public (string reply, List<string> suggestions) GetRemindersGuidance()
    {
        var reply =
            "You can manage Council Tax payment reminders and go paperless through your **MyInfo account** on the Bradford Council website.\n\n" +
            "🔗 https://www.bradford.gov.uk/benefits/myinfo/myinfo/\n\n" +
            "**Available options:**\n" +
            "• Switch to paperless bills (reminders by email)\n" +
            "• Set up Direct Debit for automatic monthly payments\n" +
            "• View your payment history and balance online\n\n" +
            "FUTURE: In-chat payment reminders will be available in a future version of this assistant.";

        return (reply, new List<string> { "Set up Direct Debit", "Register for MyInfo", "View my Council Tax balance", "Council Tax discounts" });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static string? ExtractBandLetter(string input)
    {
        input = input.ToUpperInvariant().Replace("BAND", "").Trim();
        if (input.Length == 1 && input[0] >= 'A' && input[0] <= 'H')
            return input;
        return null;
    }

    private static int ParseMissedMonths(string input)
    {
        input = input?.ToLowerInvariant().Trim() ?? "";

        if (input.Contains("more than 3") || input.Contains("4") || input.Contains("five") || input.Contains("six") || input.Contains("many"))
            return 4;

        var digits = System.Text.RegularExpressions.Regex.Match(input, @"\d+");
        if (digits.Success && int.TryParse(digits.Value, out var n))
            return n;

        return input switch
        {
            var s when s.Contains("one")   || s.Contains("1 month") => 1,
            var s when s.Contains("two")   || s.Contains("2 month") => 2,
            var s when s.Contains("three") || s.Contains("3 month") => 3,
            _ => 1
        };
    }
}

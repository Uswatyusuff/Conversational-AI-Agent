using System.Text.RegularExpressions;
using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

public class ChatOrchestrator
{
    private readonly ConversationMemory _memory;
    private readonly EmbeddingService _embed;
    private readonly RetrievalService _retrieval;
    private readonly OpenAiChatService _openAi;
    private readonly LangChainClientService _langChain;
    private readonly IConfiguration _config;

    // ── New service handlers ─────────────────────────────────────────────────────
    private readonly AppointmentService _appointments;
    private readonly FormFlowService _formFlow;
    private readonly HousingNavigatorService _housingNav;
    private readonly CouncilTaxCalculatorService _ctaxCalc;
    private readonly SchoolFinderService _schoolFinder;
    private readonly LocationService _location;

    private readonly Dictionary<string, string[]> _strongServiceTriggers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Council Tax"] = new[]
        {
            "council tax", "ctax", "tax", "bill", "balance", "arrears",
            "direct debit", "discount", "exemption", "council tax payment"
        },
        ["Waste & Bins"] = new[]
        {
            "bin", "bins", "waste", "recycling", "missed", "collection",
            "bulky", "replacement bin", "bin collection", "bin day",
            "collection day", "waste collection", "recycling collection"
        },
        ["Benefits & Support"] = new[]
        {
            "benefit", "benefits", "support", "financial support", "hardship",
            "council tax support", "housing benefit", "universal credit", "uc",
            "money help", "blue badge", "disabled badge", "disable badge",
            "mobility support", "parking badge",
            "disabled", "disability", "disabled people", "people with disabilities",
            "pip", "personal independence payment", "dla", "disability living allowance",
            "free school meals", "fsm", "welfare", "welfare advice",
            "crisis fund", "food bank", "emergency support", "cost of living"
        },
        ["Education"] = new[]
        {
            "school", "schools", "admissions", "apply for school", "deadline",
            "in-year", "transfer", "send", "ehcp", "transport", "school place"
        },["Planning"] = new[]
        
        {
            "planning", "planning application", "planning applications",
            "check planning application", "view planning application",
        "comment on planning application", "object to planning application",
        "planning permission", "building control"
        },
        ["Libraries"] = new[]
        {
            "library", "libraries", "renew library books", "renew books",
            "borrow books", "reserve books", "digital library", "e-books", "ebooks"
        },
        ["Housing"] = new[]
        {
            "housing", "homeless", "homelessness", "find a home",
            "repairs", "tenant", "landlord", "housing assistance"
        },
        ["Contact Us"] = new[]
        {
            "contact us", "contact the council", "telephone", "phone number",
            "email alerts", "call the council", "contact details"
        },

        // ── New services ──────────────────────────────────────────────────────
        ["Location"] = new[]
        {
            "nearest", "near me", "close to me", "closest", "find a library",
            "find a council office", "find a recycling centre", "recycling centre near",
            "library near", "council office near", "where is my nearest",
            "location", "directions", "how do i get to"
        },
        ["Appointment"] = new[]
        {
            "book an appointment", "book appointment", "make an appointment",
            "schedule a call", "arrange a visit", "book a call", "callback",
            "call back", "reschedule appointment", "cancel appointment",
            "speak to someone", "talk to someone"
        },
        ["Form Assistant"] = new[]
        {
            "fill in a form", "help with a form", "form help", "apply online",
            "help filling", "guided application", "start an application",
            "benefits form", "housing form", "school application form",
            "blue badge form", "council tax form"
        },
    };

    // Small-talk is now handled via IsSmallTalk() / GetSmallTalkReply() static methods below.

    public ChatOrchestrator(
        ConversationMemory memory,
        EmbeddingService embed,
        RetrievalService retrieval,
        OpenAiChatService openAi,
        LangChainClientService langChain,
        IConfiguration config,
        AppointmentService appointments,
        FormFlowService formFlow,
        HousingNavigatorService housingNav,
        CouncilTaxCalculatorService ctaxCalc,
        SchoolFinderService schoolFinder,
        LocationService location)
    {
        _memory       = memory;
        _embed        = embed;
        _retrieval    = retrieval;
        _openAi       = openAi;
        _langChain    = langChain;
        _config       = config;
        _appointments = appointments;
        _formFlow     = formFlow;
        _housingNav   = housingNav;
        _ctaxCalc     = ctaxCalc;
        _schoolFinder = schoolFinder;
        _location     = location;
    }

    public async Task<(string reply, string service, string nextStepsUrl, float score, List<string> suggestions)> HandleChatAsync(string sessionId, string message)
    {
        var normMsg = Normalize(message);
        var lastService = _memory.GetLastService(sessionId) ?? "";
        var lastIntent = _memory.GetLastIntent(sessionId) ?? "";
        var pendingFlow = _memory.GetPendingFlow(sessionId) ?? "";
        var maskedPostcode = _memory.GetMaskedPostcode(sessionId) ?? "";
        var maskedAddress = _memory.GetMaskedAddress(sessionId) ?? "";
        var activeAddress = _memory.GetActiveAddress(sessionId);
        var activePostcode = _memory.GetActivePostcode(sessionId);
        var lastBinResult = _memory.GetLastBinResult(sessionId);
        var hasAddress = _memory.GetHasSelectedAddress(sessionId);

        var detectedService = DetectService(normMsg, _strongServiceTriggers);

        // 0. Crisis / self-harm / suicide safeguard — fires before EVERY other guard,
        //    before embeddings, retrieval, and follow-up carry-over.
        if (IsCrisisIntent(normMsg))
        {
            const string crisisReply =
                "I'm really sorry you're feeling this way. I can't help with harming yourself. " +
                "Please get support right now by calling emergency services if you're in immediate danger. " +
                "If you're in the UK, call NHS 111 and select the mental health option, " +
                "or call Samaritans on 116 123 any time. " +
                "If you can, contact someone you trust and tell them you need support now.";

            var crisisSuggestions = new List<string>
            {
                "I need urgent mental health help",
                "Call Samaritans",
                "I want general support"
            };

            SaveConversation(sessionId, message, crisisReply, "Crisis Support", "crisis", crisisSuggestions);
            return (crisisReply, "Crisis Support", "", 0f, crisisSuggestions);
        }

        // 1. Small-talk — must run before anything that touches lastService context
        if (IsSmallTalk(normMsg))
        {
            var smallTalkReply = GetSmallTalkReply(normMsg);
            var smallTalkSuggestions = new List<string>
            {
                "How do I check my Council Tax balance?",
                "When is my bin collection?",
                "How do I apply for a Blue Badge?",
                "How do I apply for free school meals?"
            };
            SaveConversation(sessionId, message, smallTalkReply, "Unknown", "greeting", smallTalkSuggestions);
            return (smallTalkReply, "Unknown", "", 0, smallTalkSuggestions);
        }

        // 2. Meaningless / keyboard-mash input — stop before embeddings
        if (IsMeaninglessInput(normMsg))
        {
            var reply = "I’m not sure what you mean yet. You can ask me about Council Tax, bins, benefits, housing, schools, planning, or libraries.";
            var suggestions = new List<string>
            {
                "How do I pay my Council Tax?",
                "When is my bin collection?",
                "How do I apply for benefits?",
                "How do I get housing help?"
            };
            SaveConversation(sessionId, message, reply, "Unknown", "clarification", suggestions);
            return (reply, "Unknown", "", 0f, suggestions);
        }

        // 3. Vague help requests — no service signal, respond with a service menu
        if (IsVagueHelpRequest(normMsg))
        {
            var reply = "Of course — I'm here to help! What would you like assistance with?";
            var suggestions = new List<string>
            {
                "How do I pay my Council Tax?",
                "When is my bin collection?",
                "How do I apply for benefits?",
                "How do I get housing help?"
            };
            SaveConversation(sessionId, message, reply, "Unknown", "vague_help", suggestions);
            return (reply, "Unknown", "", 0f, suggestions);
        }

        // 5. Short follow-up carry-over — runs AFTER the early guards so greetings are never
        //    contaminated by a previous service context (e.g. "hi" → "council tax hi").
        //    IMPORTANT: only carry over context when there is actual history in THIS session.
        //    If the session has zero turns the user is starting a brand-new conversation; a
        //    short message like "yes" or "more info" must not inherit stale state from a
        //    previous chat that happened to share the same sessionId (which should now be
        //    impossible after the resetChat() fix, but we defend in depth here too).
        var currentTurns = _memory.GetRecentTurns(sessionId);
        if (currentTurns.Count > 0 &&
            string.IsNullOrWhiteSpace(detectedService) &&
            IsShortFollowUp(normMsg) &&
            !string.IsNullOrWhiteSpace(lastService))
        {
            normMsg = $"{Normalize(lastService)} {normMsg}";
            detectedService = DetectService(normMsg, _strongServiceTriggers);
        }
        else if (currentTurns.Count == 0 && IsShortFollowUp(normMsg))
        {
            // Fresh session with a vague opener — ask what the user needs rather than guessing
            var clarificationReply =
                "I can help with that — could you tell me a bit more? " +
                "For example, is this about Council Tax, bins, benefits, housing, planning, or something else?";
            var clarificationSuggestions = new List<string>
            {
                "Council Tax",
                "Bins & recycling",
                "Benefits & support",
                "Housing help"
            };
            SaveConversation(sessionId, message, clarificationReply, "Unknown", "clarification", clarificationSuggestions);
            return (clarificationReply, "Unknown", "", 0.5f, clarificationSuggestions);
        }

        // 3. Reset conversational context when user clearly changes topic
if (IsContextResetIntent(normMsg))
{
    _memory.ClearPendingFlow(sessionId);
    _memory.ClearAddressContext(sessionId);
    _memory.SetHasSelectedAddress(sessionId, false);
    _memory.SetLastBinResult(sessionId, "");

    var reply = "Of course. What would you like to ask about next?";
    var resetSuggestions = new List<string>
    {
        "Council Tax",
        "Waste & Bins",
        "Benefits & Support",
        "Planning"
    };

    SaveConversation(sessionId, message, reply, "Unknown", "context_reset", resetSuggestions);
    return (reply, "Unknown", "", 1.0f, resetSuggestions);
}

        // 2. Continue short follow-up with previous service context
                // 3. Use the previously selected address
        if (IsSameAddressIntent(normMsg) &&
            hasAddress &&
            !string.IsNullOrWhiteSpace(lastBinResult))
        {
            var reply = $"Here are the bin collection details for your previously selected address:\n\n{lastBinResult}";

            var sameAddressSuggestions = new List<string>
            {
                "Tell me about general waste",
                "Tell me about recycling",
                "Tell me about garden waste",
                "Use different address"
            };

            SaveConversation(sessionId, message, reply, "Waste & Bins", "bin_result_follow_up", sameAddressSuggestions);
            return (reply, "Waste & Bins", "", 1.0f, sameAddressSuggestions);
        }

        // 4. Use a different address
        if (IsDifferentAddressIntent(normMsg))
        {
            _memory.SetPendingFlow(sessionId, "awaiting_postcode_for_bin_collection");

            var reply = "Please enter a different postcode so I can look up another address.";

            var differentAddressSuggestions = new List<string>
            {
                "Enter postcode"
            };

            SaveConversation(sessionId, message, reply, "Waste & Bins", "new_postcode", differentAddressSuggestions);
            return (reply, "Waste & Bins", "", 1.0f, differentAddressSuggestions);
        }
        
        if (IsEnterPostcodeIntent(normMsg) &&
            string.Equals(lastService, "Waste & Bins", StringComparison.OrdinalIgnoreCase))
        {
            _memory.SetPendingFlow(sessionId, "awaiting_postcode_for_bin_collection");

            var reply = "Please enter your postcode so I can look up the address options for your bin collection day.";
            var postcodePromptSuggestions = new List<string>
            {
                "BD3 8PX"
            };

            SaveConversation(sessionId, message, reply, "Waste & Bins", "postcode_prompt", postcodePromptSuggestions);
            return (reply, "Waste & Bins", "", 1.0f, postcodePromptSuggestions);
        }
        if (IsMissedBinIntent(normMsg))
{
    var reply = "To report a missed bin, fill in the missed bin form or call 01274 431000. You may need to register before using the form.";
    var suggestions = new List<string>
    {
        "When is my bin collection?",
        "Report a missed bin",
        "Request a new bin"
    };

    SaveConversation(sessionId, message, reply, "Waste & Bins", "missed_bin", suggestions);
    return (reply, "Waste & Bins", "", 1.0f, suggestions);
}



        // 5. Follow-up questions about the previously selected address
        if (IsBinFollowUpIntent(normMsg) &&
            hasAddress &&
            !string.IsNullOrWhiteSpace(lastBinResult))
        {
            var reply = BuildBinFollowUpReply(normMsg, activeAddress ?? "", lastBinResult ?? "");

            var binFollowUpSuggestions = new List<string>
            {
                "Tell me about general waste",
                "Tell me about recycling",
                "Tell me about garden waste",
                "Use different address"
            };

            SaveConversation(sessionId, message, reply, "Waste & Bins", "bin_result_follow_up", binFollowUpSuggestions);
            return (reply, "Waste & Bins", "", 1.0f, binFollowUpSuggestions);
        }
        //         // 6. Generic waste follow-up only when no more specific address/bin-result intent matched
        // if (IsWasteFollowUpIntent(normMsg) &&
        //     !LooksLikeUkPostcode(message) &&
        //     !IsBinCollectionDayIntent(normMsg) &&
        //     !IsSameAddressIntent(normMsg) &&
        //     !IsDifferentAddressIntent(normMsg) &&
        //     !IsBinFollowUpIntent(normMsg) &&
        //     (!string.IsNullOrWhiteSpace(maskedPostcode) || hasAddress))
        // {
        //     var reply = hasAddress
        //         ? "You have a previously selected address in this chat. Would you like to use the same address, or check a different one?"
        //         : "You were previously asking about a waste query. Would you like to use the same postcode, or check a different one?";

        //     var wasteFollowUpSuggestions = new List<string>
        //     {
        //         hasAddress ? "Use same address" : "Use same postcode",
        //         "Use different address",
        //         "When is my bin collection?",
        //         "Report a missed bin"
        //     };

        //     SaveConversation(sessionId, message, reply, "Waste & Bins", "waste_follow_up", wasteFollowUpSuggestions);
        //     return (reply, "Waste & Bins", "", 1.0f, wasteFollowUpSuggestions);
        // }
        // // 
        // 6. Generic waste follow-up only for address/collection continuation

     if (IsWasteFollowUpIntent(normMsg) &&
           !LooksLikeUkPostcode(message) &&
            !IsNewBinRequestIntent(normMsg) &&
            !normMsg.Contains("cost") &&
            !normMsg.Contains("price") &&
            !normMsg.Contains("how much") &&
            !normMsg.Contains("apply") &&
            !normMsg.Contains("request") &&
        !IsBinCollectionDayIntent(normMsg) &&
        !IsSameAddressIntent(normMsg) &&
        !IsDifferentAddressIntent(normMsg) &&
        !IsBinFollowUpIntent(normMsg) &&
        (!string.IsNullOrWhiteSpace(maskedPostcode) || hasAddress))
    {
    var reply = hasAddress
        ? "You have a previously selected address in this chat. Would you like to use the same address, or check a different one?"
        : "You were previously asking about a waste query. Would you like to use the same postcode, or check a different one?";

    var wasteFollowUpSuggestions = new List<string>
    {
        hasAddress ? "Use same address" : "Use same postcode",
        "Use different address",
        "When is my bin collection?",
        "Report a missed bin"
    };

    SaveConversation(sessionId, message, reply, "Waste & Bins", "waste_follow_up", wasteFollowUpSuggestions);
    return (reply, "Waste & Bins", "", 1.0f, wasteFollowUpSuggestions);
    }
        // 4. Special bin collection flow
        if (normMsg.Contains("sunday") && normMsg.Contains("bin"))
    {
    _memory.SetPendingFlow(sessionId, "awaiting_postcode_for_bin_collection");

    var reply = "Bin collection days depend on your address. Please enter your postcode so I can check your collection schedule.";
    var suggestions = new List<string>
    {
        "BD3 8PX",
        "Report a missed bin"
    };

    SaveConversation(sessionId, message, reply, "Waste & Bins", "bin_collection_lookup", suggestions);
    return (reply, "Waste & Bins", "", 1.0f, suggestions);
    }

        if (IsBinCollectionDayIntent(normMsg))
        {
            _memory.SetPendingFlow(sessionId, "awaiting_postcode_for_bin_collection");

            var reply = "Please enter your postcode so I can look up the address options for your bin collection day.";
            var binCollectionSuggestions = new List<string>
            {
                "Enter postcode",
                "Report a missed bin",
                "Request a new bin"
            };

            SaveConversation(sessionId, message, reply, "Waste & Bins", "bin_collection_lookup", binCollectionSuggestions);
            return (reply, "Waste & Bins", "", 1.0f, binCollectionSuggestions);
        }

        // 5. If user enters a postcode after bin/waste flow, send special frontend signal
        if (LooksLikeUkPostcode(message) &&
            (string.Equals(lastService, "Waste & Bins", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(pendingFlow, "awaiting_postcode_for_bin_collection", StringComparison.OrdinalIgnoreCase)))
        {
            var postcode = message.Trim().ToUpperInvariant();
            _memory.SetMaskedPostcode(sessionId, postcode);
            _memory.SetPendingFlow(sessionId, "postcode_lookup_started");

            var reply = $"POSTCODE_LOOKUP::{postcode}";
            var postcodeSuggestions = new List<string>();

            SaveConversation(sessionId, message, reply, "Waste & Bins", "postcode_lookup", postcodeSuggestions);
            return (reply, "Waste & Bins", "", 1.0f, postcodeSuggestions);
        }

        // ── NEW SERVICE FLOWS ─────────────────────────────────────────────────────

        // ── Location lookup flow ──────────────────────────────────────────────────
        if (IsLocationLookupIntent(normMsg))
        {
            var locType = DetectLocationSubType(normMsg);
            _memory.SetPendingFlow(sessionId, "awaiting_postcode_for_location");
            _memory.SetLocationLookupType(sessionId, locType);
            _memory.SetLastService(sessionId, "Location");

            var locPrompt = locType switch
            {
                "library"          => "Please enter your postcode and I'll find the nearest libraries for you.",
                "recycling_centre" => "Please enter your postcode and I'll find the nearest household waste recycling centres.",
                "council_office"   => "Please enter your postcode and I'll find the nearest council offices.",
                "school"           => "Please enter your postcode and I'll find nearby schools.",
                _                  => "Please enter your postcode and I'll find your nearest council services (offices, libraries, and recycling centres)."
            };

            var locSuggestions = new List<string> { "BD1 1HY", "BD3 8PX", "Find council office", "Find library" };
            SaveConversation(sessionId, message, locPrompt, "Location", "location_lookup", locSuggestions);
            return (locPrompt, "Location", "https://www.bradford.gov.uk/contact-us/", 1.0f, locSuggestions);
        }

        // ── Postcode entered during location flow ─────────────────────────────────
        if (LooksLikeUkPostcode(message) &&
            string.Equals(pendingFlow, "awaiting_postcode_for_location", StringComparison.OrdinalIgnoreCase))
        {
            var postcode  = message.Trim().ToUpperInvariant();
            var locType   = _memory.GetLocationLookupType(sessionId);
            _memory.SetMaskedPostcode(sessionId, postcode);
            _memory.SetPendingFlow(sessionId, "location_lookup_started");

            var signal = $"LOCATION_LOOKUP::{postcode}::{locType}";
            SaveConversation(sessionId, message, signal, "Location", "location_lookup", new List<string>());
            return (signal, "Location", "", 1.0f, new List<string>());
        }

        // ── Appointment booking flow ──────────────────────────────────────────────
        if (IsAppointmentIntent(normMsg))
        {
            _memory.ClearPendingFlow(sessionId);
            _memory.ClearAddressContext(sessionId);
            _memory.ClearAppointmentFlow(sessionId);

            _memory.SetPendingFlow(sessionId, "appointment:select_type");
            _memory.SetLastService(sessionId, "Appointment");

            var typeNames = _appointments.GetAppointmentTypeNames();
            var reply = "I can book an appointment with Bradford Council for you.\n\nWhat type of appointment do you need?";
            var suggestions = typeNames.Take(4).ToList();

            SaveConversation(sessionId, message, reply, "Appointment", "appointment_start", suggestions);
            return (reply, "Appointment", "", 1.0f, suggestions);
        }

        // ── Appointment cancellation — catches cancel at ANY step ─────────────────
        if (pendingFlow.StartsWith("appointment:", StringComparison.OrdinalIgnoreCase)
            && IsAppointmentCancelIntent(normMsg))
        {
            _memory.ClearAppointmentFlow(sessionId);
            _memory.ClearPendingFlow(sessionId);
            var cancelSuggestions = new List<string> { "Book an appointment", "Council Tax", "Housing", "Benefits & Support" };
            const string cancelReply = "Okay, I've cancelled that appointment request. What would you like help with next?";
            SaveConversation(sessionId, message, cancelReply, "Appointment", "appointment_cancelled", cancelSuggestions);
            return (cancelReply, "Appointment", "", 1.0f, cancelSuggestions);
        }

        // ── Appointment flow escape — if user asks a real question mid-booking, ──
        // release them from the flow rather than treating every message as an
        // appointment step response.
        if (pendingFlow.StartsWith("appointment:", StringComparison.OrdinalIgnoreCase)
            && IsUnrelatedToAppointmentFlow(normMsg))
        {
            _memory.ClearAppointmentFlow(sessionId);
            _memory.ClearPendingFlow(sessionId);
            // Fall through — the message will route normally below
        }

        if (string.Equals(pendingFlow, "appointment:select_type", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = _appointments.ResolveType(message.Trim());
            if (resolved == null)
            {
                var typeNames = _appointments.GetAppointmentTypeNames();
                var clarify = "I didn't recognise that appointment type. Please choose from the options below:";
                return (clarify, "Appointment", "", 1.0f, typeNames.Take(4).ToList());
            }

            _memory.SetAppointmentType(sessionId, resolved.Name);
            _memory.SetPendingFlow(sessionId, "appointment:select_date");

            var dates     = _appointments.GetAvailableDates(5);
            var reply     = $"You've chosen: **{resolved.Name}** ({resolved.DurationMinutes} minutes).\n\nWhich date would you like?";
            var dateSuggestions = dates.Take(4).ToList();

            SaveConversation(sessionId, message, reply, "Appointment", "appointment_type_selected", dateSuggestions);
            return (reply, "Appointment", "", 1.0f, dateSuggestions);
        }

        if (string.Equals(pendingFlow, "appointment:select_date", StringComparison.OrdinalIgnoreCase))
        {
            _memory.SetAppointmentDate(sessionId, message.Trim());
            _memory.SetPendingFlow(sessionId, "appointment:select_time");

            var times     = _appointments.GetAvailableTimes();
            var reply     = $"Date: **{message.Trim()}**\n\nWhat time would you like?";
            var timeSuggestions = times.Take(6).ToList();

            SaveConversation(sessionId, message, reply, "Appointment", "appointment_date_selected", timeSuggestions);
            return (reply, "Appointment", "", 1.0f, timeSuggestions);
        }

        if (string.Equals(pendingFlow, "appointment:select_time", StringComparison.OrdinalIgnoreCase))
        {
            _memory.SetAppointmentTime(sessionId, message.Trim());
            _memory.SetPendingFlow(sessionId, "appointment:enter_name");

            var reply = $"Time: **{message.Trim()}**\n\nPlease enter your full name for the booking:";
            SaveConversation(sessionId, message, reply, "Appointment", "appointment_time_selected", new List<string>());
            return (reply, "Appointment", "", 1.0f, new List<string>());
        }

        if (string.Equals(pendingFlow, "appointment:enter_name", StringComparison.OrdinalIgnoreCase))
        {
            _memory.SetAppointmentName(sessionId, message.Trim());
            _memory.SetPendingFlow(sessionId, "appointment:enter_phone");

            var reply = $"Name: **{message.Trim()}**\n\nPlease enter your phone number:";
            SaveConversation(sessionId, message, reply, "Appointment", "appointment_name_entered", new List<string>());
            return (reply, "Appointment", "", 1.0f, new List<string>());
        }

        if (string.Equals(pendingFlow, "appointment:enter_phone", StringComparison.OrdinalIgnoreCase))
        {
            _memory.SetAppointmentPhone(sessionId, message.Trim());
            _memory.SetPendingFlow(sessionId, "appointment:enter_email");

            var reply = $"Phone: **{message.Trim()}**\n\nPlease enter your email address:";
            SaveConversation(sessionId, message, reply, "Appointment", "appointment_phone_entered", new List<string>());
            return (reply, "Appointment", "", 1.0f, new List<string>());
        }

        if (string.Equals(pendingFlow, "appointment:enter_email", StringComparison.OrdinalIgnoreCase))
        {
            _memory.SetAppointmentEmail(sessionId, message.Trim());
            _memory.SetPendingFlow(sessionId, "appointment:confirm");

            var apptType  = _memory.GetAppointmentType(sessionId);
            var apptDate  = _memory.GetAppointmentDate(sessionId);
            var apptTime  = _memory.GetAppointmentTime(sessionId);
            var apptName  = _memory.GetAppointmentName(sessionId);
            var apptPhone = _memory.GetAppointmentPhone(sessionId);

            var reply =
                $"Please confirm your appointment:\n\n" +
                $"• **Type:** {apptType}\n" +
                $"• **Date:** {apptDate}\n" +
                $"• **Time:** {apptTime}\n" +
                $"• **Name:** {apptName}\n" +
                $"• **Phone:** {apptPhone}\n" +
                $"• **Email:** {message.Trim()}\n\n" +
                $"Shall I confirm this booking?";

            SaveConversation(sessionId, message, reply, "Appointment", "appointment_review", new List<string> { "Confirm booking", "Cancel" });
            return (reply, "Appointment", "", 1.0f, new List<string> { "Confirm booking", "Cancel" });
        }

        if (string.Equals(pendingFlow, "appointment:confirm", StringComparison.OrdinalIgnoreCase))
        {
            if (normMsg.Contains("confirm") || normMsg.Contains("yes") || normMsg.Contains("book it"))
            {
                var bookingData = new Models.AppointmentBookingData
                {
                    SessionId       = sessionId,
                    AppointmentType = _memory.GetAppointmentType(sessionId),
                    Date            = _memory.GetAppointmentDate(sessionId),
                    Time            = _memory.GetAppointmentTime(sessionId),
                    Name            = _memory.GetAppointmentName(sessionId),
                    Phone           = _memory.GetAppointmentPhone(sessionId),
                    Email           = _memory.GetAppointmentEmail(sessionId),
                };

                var confirmation = _appointments.ConfirmBooking(bookingData);
                _memory.ClearAppointmentFlow(sessionId);
                _memory.ClearPendingFlow(sessionId);

                var confSuggestions = new List<string> { "Book another appointment", "Council Tax", "Housing", "Benefits & Support" };
                SaveConversation(sessionId, message, confirmation.Message, "Appointment", "appointment_confirmed", confSuggestions);
                return (confirmation.Message, "Appointment", "", 1.0f, confSuggestions);
            }
            else
            {
                // User said no / cancel
                _memory.ClearAppointmentFlow(sessionId);
                _memory.ClearPendingFlow(sessionId);
                var cancelReply = "Booking cancelled. Is there anything else I can help you with?";
                var cancelSuggestions = new List<string> { "Book an appointment", "Council Tax", "Housing", "Benefits & Support" };
                SaveConversation(sessionId, message, cancelReply, "Appointment", "appointment_cancelled", cancelSuggestions);
                return (cancelReply, "Appointment", "", 1.0f, cancelSuggestions);
            }
        }

        // ── Form flow (active session — user is answering a form question) ─────────
       // ── Form cancel ─────────────────────────────────────────────────────────────
            if (_formFlow.HasActiveForm(sessionId) && IsFormCancelIntent(normMsg))
            {
                _formFlow.ClearSession(sessionId);
                _memory.ClearPendingFlow(sessionId);

                var cancelReply = "Okay, I've cancelled that form. What would you like help with next?";
                var cancelSuggestions = new List<string> { "Benefits form", "Housing form", "School form", "Blue Badge form" };

                SaveConversation(sessionId, message, cancelReply, "Form Assistant", "form_cancelled", cancelSuggestions);
                return (cancelReply, "Form Assistant", "", 1.0f, cancelSuggestions);
            }

            // ── Form flow (active session — user is answering a form question) ─────────
            if (_formFlow.HasActiveForm(sessionId))
            {
                var stepResult = _formFlow.SubmitAnswer(sessionId, message.Trim());

                if (stepResult.IsComplete && stepResult.Summary != null)
                {
                    _memory.ClearPendingFlow(sessionId);
                    var formReply = BuildFormSummaryReply(stepResult.Summary);
                    var formSuggestions = new List<string> { "Submit application", "Start another form", "Housing", "Benefits & Support" };
                    SaveConversation(sessionId, message, formReply, "Form Assistant", "form_complete", formSuggestions);
                    return (formReply, "Form Assistant", stepResult.Summary.NextStepsUrl, 1.0f, formSuggestions);
                }
                else
                {
                    var progress = $"({stepResult.StepNumber}/{stepResult.TotalSteps})";
                    var formReply = $"{progress} {stepResult.NextQuestion}";
                    if (!string.IsNullOrWhiteSpace(stepResult.Hint))
                        formReply += $"\n\n💡 *{stepResult.Hint}*";

                    var nextSuggestions = stepResult.Options.Any()
                        ? stepResult.Options.Take(4).ToList()
                        : new List<string> { "Cancel form" };

                    SaveConversation(sessionId, message, formReply, "Form Assistant", "form_step", nextSuggestions);
                    return (formReply, "Form Assistant", "", 1.0f, nextSuggestions);
                }
            }
        // ── Form flow start intent ────────────────────────────────────────────────
        if (IsFormStartIntent(normMsg))
        {
            var formType = DetectFormType(normMsg);

            if (string.IsNullOrWhiteSpace(formType))
            {
                var formPickReply = "I can guide you through these applications step by step:\n\n" +
                    "• **Benefits** — Housing Benefit & Council Tax Support\n" +
                    "• **Housing** — Housing application\n" +
                    "• **School** — School place application\n" +
                    "• **Blue Badge** — Blue Badge application\n" +
                    "• **Council Tax change** — Change of address or circumstances\n\n" +
                    "Which form would you like to start?";

                var formSuggestions = new List<string> { "Benefits form", "Housing form", "School form", "Blue Badge form" };
                SaveConversation(sessionId, message, formPickReply, "Form Assistant", "form_select", formSuggestions);
                return (formPickReply, "Form Assistant", "", 1.0f, formSuggestions);
            }

            var startResult = _formFlow.StartForm(sessionId, formType);
            _memory.SetLastService(sessionId, "Form Assistant");
            _memory.SetPendingFlow(sessionId, "form_in_progress");

            var title   = _formFlow.GetFormTitle(formType);
            var intro   = $"I'll guide you through the **{title}** step by step.\n\n" +
                          $"(1/{startResult.TotalSteps}) {startResult.NextQuestion}";
            if (!string.IsNullOrWhiteSpace(startResult.Hint))
                intro += $"\n\n💡 *{startResult.Hint}*";

            var startSuggestions = startResult.Options.Any()
                ? startResult.Options.Take(4).ToList()
                : new List<string> { "Cancel form" };

            SaveConversation(sessionId, message, intro, "Form Assistant", "form_started", startSuggestions);
            return (intro, "Form Assistant", "", 1.0f, startSuggestions);
        }

        // ── Housing navigator (urgent flows) ─────────────────────────────────────
        var routingService = !string.IsNullOrWhiteSpace(detectedService) ? detectedService : lastService;

if (string.Equals(routingService, "Housing", StringComparison.OrdinalIgnoreCase) ||
    IsHousingUrgentIntent(normMsg))
{
    var housingNode = _housingNav.DetectHousingNode(normMsg);

    if (!string.IsNullOrWhiteSpace(housingNode) &&
        housingNode != HousingNavigatorService.NodeGeneral)
    {
        var (housingReply, housingSuggestions, housingUrl) = _housingNav.GetNodeResponse(housingNode);
        _memory.SetHousingFlowNode(sessionId, housingNode);
        _memory.SetLastService(sessionId, "Housing");

        SaveConversation(sessionId, message, housingReply, "Housing", housingNode, housingSuggestions);
        return (housingReply, "Housing", housingUrl, 1.0f, housingSuggestions);
    }
}

        // ── School finder intent (find schools near postcode) ─────────────────────
        // ── School finder intent (find schools near postcode) ─────────────────────
        if (IsSchoolFinderIntent(normMsg))
        {
            var schoolType = normMsg.Contains("primary") ? "primary" :
                            normMsg.Contains("secondary") ? "secondary" : "all";

            if (LooksLikeUkPostcode(message))
            {
                var postcode = message.Trim().ToUpperInvariant();
                var schools = _schoolFinder.FindNearby(postcode, schoolType);
                var schoolReply = BuildSchoolResultsReply(schools, postcode);
                var schoolSuggestions = new List<string> { "Primary schools", "Secondary schools", "School admissions", "Apply for a school place" };

                SaveConversation(sessionId, message, schoolReply, "Education", "school_finder", schoolSuggestions);
                return (schoolReply, "Education", "https://www.bradford.gov.uk/education-and-skills/school-admissions/", 1.0f, schoolSuggestions);
            }

            var wantsSchoolResults =
                normMsg.Contains("find") ||
                normMsg.Contains("search") ||
                normMsg.Contains("show") ||
                normMsg.Contains("list") ||
                normMsg.Contains("near me") ||
                normMsg.Contains("nearby") ||
                normMsg.Contains("schools near") ||
                normMsg.Contains("primary schools") ||
                normMsg.Contains("secondary schools");

            if (wantsSchoolResults)
            {
                _memory.SetPendingFlow(sessionId, "awaiting_postcode_for_school_finder");
                _memory.SetLocationLookupType(sessionId, schoolType);
                _memory.SetLastService(sessionId, "Education");

                var reply = schoolType switch
                {
                    "primary" => "Please enter your postcode and I'll find nearby primary schools.",
                    "secondary" => "Please enter your postcode and I'll find nearby secondary schools.",
                    _ => "Please enter your postcode and I'll find nearby schools."
                };

                var suggestions = new List<string> { "BD1 1HY", "BD3 8PX" };
                SaveConversation(sessionId, message, reply, "Education", "school_finder_postcode_prompt", suggestions);
                return (reply, "Education", "https://www.bradford.gov.uk/education-and-skills/school-admissions/", 1.0f, suggestions);
            }

            // Admissions/general school info: fall through to RAG rather than a fixed reply.
            if (string.IsNullOrWhiteSpace(detectedService))
                detectedService = "Education";
            _memory.SetLastService(sessionId, "Education");
        }

        if (LooksLikeUkPostcode(message) &&
            string.Equals(pendingFlow, "awaiting_postcode_for_school_finder", StringComparison.OrdinalIgnoreCase))
        {
            var postcode = message.Trim().ToUpperInvariant();
            var schoolType = _memory.GetLocationLookupType(sessionId);

            var schools = _schoolFinder.FindNearby(postcode, schoolType);
            var schoolReply = BuildSchoolResultsReply(schools, postcode);
            var schoolSuggestions = new List<string> { "Primary schools", "Secondary schools", "School admissions", "Apply for a school place" };

            _memory.ClearPendingFlow(sessionId);
            SaveConversation(sessionId, message, schoolReply, "Education", "school_finder", schoolSuggestions);
            return (schoolReply, "Education", "https://www.bradford.gov.uk/education-and-skills/school-admissions/", 1.0f, schoolSuggestions);
        }
        

        // ── Council Tax calculator / arrears flow ─────────────────────────────────
        if (_ctaxCalc.IsCalculatorIntent(normMsg) && !IsAppointmentIntent(normMsg))
        {
            var storedBill = _memory.GetCtaxMonthlyBill(sessionId);

            if (storedBill > 0 && string.Equals(pendingFlow, CouncilTaxCalculatorService.FlowAwaitingMissed,
                StringComparison.OrdinalIgnoreCase))
            {
                var (planReply, planSuggestions) = _ctaxCalc.GeneratePaymentPlan(storedBill, message.Trim());
                _memory.ClearPendingFlow(sessionId);
                _memory.SetCtaxMonthlyBill(sessionId, 0m);
                SaveConversation(sessionId, message, planReply, "Council Tax", "ctax_payment_plan", planSuggestions);
                return (planReply, "Council Tax", "https://www.bradford.gov.uk/council-tax/council-tax/", 1.0f, planSuggestions);
            }

            var (startReply, startSuggestions) = _ctaxCalc.StartCalculatorFlow();
            _memory.SetPendingFlow(sessionId, CouncilTaxCalculatorService.FlowAwaitingBill);
            _memory.SetLastService(sessionId, "Council Tax");
            SaveConversation(sessionId, message, startReply, "Council Tax", "ctax_calc_start", startSuggestions);
            return (startReply, "Council Tax", "", 1.0f, startSuggestions);
        }

        if (string.Equals(pendingFlow, CouncilTaxCalculatorService.FlowAwaitingBill,
            StringComparison.OrdinalIgnoreCase))
        {
            var (billReply, billSuggestions, parsedAmount) = _ctaxCalc.ProcessBillInput(message.Trim());
            if (parsedAmount.HasValue)
            {
                _memory.SetCtaxMonthlyBill(sessionId, parsedAmount.Value);
                _memory.SetPendingFlow(sessionId, CouncilTaxCalculatorService.FlowAwaitingMissed);
            }
            SaveConversation(sessionId, message, billReply, "Council Tax", "ctax_bill_input", billSuggestions);
            return (billReply, "Council Tax", "", 1.0f, billSuggestions);
        }

        if (string.Equals(pendingFlow, CouncilTaxCalculatorService.FlowAwaitingMissed,
            StringComparison.OrdinalIgnoreCase))
        {
            var storedBill2 = _memory.GetCtaxMonthlyBill(sessionId);
            if (storedBill2 > 0)
            {
                var (planReply2, planSuggestions2) = _ctaxCalc.GeneratePaymentPlan(storedBill2, message.Trim());
                _memory.ClearPendingFlow(sessionId);
                _memory.SetCtaxMonthlyBill(sessionId, 0m);
                SaveConversation(sessionId, message, planReply2, "Council Tax", "ctax_payment_plan", planSuggestions2);
                return (planReply2, "Council Tax", "https://www.bradford.gov.uk/council-tax/council-tax/", 1.0f, planSuggestions2);
            }
        }

        if (_ctaxCalc.IsArrearsIntent(normMsg))
        {
            // Arrears guidance: fall through to RAG rather than returning a fixed reply.
            if (string.IsNullOrWhiteSpace(detectedService))
                detectedService = "Council Tax";
            _memory.SetLastService(sessionId, "Council Tax");
        }

        // ── Smart Bin Assistant — enhanced missed bin / bin type guide ─────────────
        if (IsBinTypeGuideIntent(normMsg))
        {
            var (binGuideReply, binGuideSuggestions) = GetBinTypeGuide(normMsg);
            SaveConversation(sessionId, message, binGuideReply, "Waste & Bins", "bin_guide", binGuideSuggestions);
            return (binGuideReply, "Waste & Bins", "https://www.bradford.gov.uk/recycling-and-waste/wheeled-bins-and-recycling-containers/what-goes-in-your-bins/", 1.0f, binGuideSuggestions);
        }

        // ── Contact Us — direct handler, no RAG needed ───────────────────────────
        if (string.Equals(detectedService, "Contact Us", StringComparison.OrdinalIgnoreCase) ||
            IsContactUsIntent(normMsg))
        {
            var contactReply = GetContactUsReply(normMsg);
            var contactSuggestions = new List<string>
            {
                "How do I pay my Council Tax?",
                "When is my bin collection?",
                "How do I apply for benefits?",
                "How do I book an appointment?"
            };
            SaveConversation(sessionId, message, contactReply, "Contact Us", "contact_us", contactSuggestions);
            return (contactReply, "Contact Us",
                "https://www.bradford.gov.uk/contact-us/",
                1.0f, contactSuggestions);
        }

        // ── Planning application status — direct answer to avoid RAG miss ────────
        if (IsPlanningApplicationStatusIntent(normMsg))
        {
            var planningReply =
                "You can check the status of a planning application on Bradford Council's planning portal. " +
                "Search by application number, address, or postcode to see the current status, documents, and any decisions made.";
            var planningSuggestions = new List<string>
            {
                "How do I comment on a planning application?",
                "How do I apply for planning permission?",
                "What is building control?",
                "How do I appeal a planning decision?"
            };
            SaveConversation(sessionId, message, planningReply, "Planning", "planning_status", planningSuggestions);
            return (planningReply, "Planning",
                "https://www.bradford.gov.uk/planning-and-building-control/planning-applications/search-for-a-planning-application/",
                1.0f, planningSuggestions);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // detect detectedService AFTER new service checks so new services take priority

        // 7. If no direct service is detected, lean on last service for short continuation
        if (string.IsNullOrWhiteSpace(detectedService) && !string.IsNullOrWhiteSpace(lastService) && IsLikelyContinuation(normMsg))
        {
            detectedService = lastService;
        }

        // 8. Embed query
        var qEmb = await _embed.EmbedAsync(message);

        // When the service was identified from a trigger keyword (not inferred from conversation
        // history), we already know the domain with high confidence. Use a lower threshold so
        // specific questions like "I want to pay council tax" get a direct answer rather than
        // triggering clarification. The strict 0.60 + gap check is reserved for ambiguous queries
        // where the service was not explicitly stated.
        var isServiceConfident = !string.IsNullOrWhiteSpace(detectedService) &&
                                 !string.Equals(detectedService, "Unknown", StringComparison.OrdinalIgnoreCase) &&
                                 string.IsNullOrWhiteSpace(lastService) == false
                                     ? true
                                     : DetectService(normMsg, _strongServiceTriggers) == detectedService &&
                                       !string.IsNullOrWhiteSpace(detectedService);
        var threshold = isServiceConfident
            ? _config.GetValue("Retrieval:ThresholdConfident", 0.45f)
            : _config.GetValue("Retrieval:Threshold", 0.60f);

        // 9. Retrieve candidate chunks
        List<(FaqChunk chunk, float score)> top =
            !string.IsNullOrWhiteSpace(detectedService)
                ? _retrieval.TopKInService(qEmb, detectedService, 4)
                : _retrieval.TopK(qEmb, 4);

        var best = top.FirstOrDefault();
        var bestChunk = best.chunk;
        var bestScore = best.score;
        var secondScore = top.Count > 1 ? top[1].score : 0f;

        // 10. Build context
        var context = top
            .Where(t => t.chunk != null)
            .Select(t => (
                title: t.chunk.Title ?? "",
                text: t.chunk.Text ?? "",
                nextUrl: t.chunk.NextStepsUrl ?? ""
            ))
            .ToList();

        var history = _memory.GetRecentTurns(sessionId, 6)
            .Select(t => (role: t.Role ?? "user", message: t.Message ?? ""))
            .ToList();

        // 11. Build service hint
        var serviceHint =
            !string.IsNullOrWhiteSpace(detectedService) ? detectedService :
            !string.IsNullOrWhiteSpace(lastService) ? lastService :
            bestChunk?.Service ?? "Unknown";

        var detectedIntent = DetectIntent(normMsg, serviceHint);

        // 12. Weak retrieval handling.
        //
        // When the service is KNOWN (detected from trigger words or strong context), we must
        // not immediately give up and return a clarification — the RAG chunks may simply be
        // sparse (Planning, Libraries) or the question may be phrased differently from the FAQ.
        // In that case, give the LangChain agent a chance to produce an answer from whatever
        // partial context we have, plus its own training knowledge.
        //
        // Only fall straight to clarification when:
        //   a) service is unknown/ambiguous, OR
        //   b) the agent also fails to produce a usable answer.
        //
        // The gap check only applies when service is ambiguous (it guards against a weakly-
        // retrieved chunk from the wrong service being used as context).
        if (bestChunk == null || bestScore < threshold ||
            (!isServiceConfident && (bestScore - secondScore) < 0.03f))
        {
            // ── Attempt: let the agent try with whatever context we have ─────────
            if (isServiceConfident &&
                !string.Equals(serviceHint, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                var agentAttempt = await _langChain.RunAgentAsync(message, serviceHint, context, history);

                var attemptToolHandled = HandleAgentToolResponse(
                    sessionId, message, agentAttempt, bestScore);
                if (attemptToolHandled.hasToolResponse)
                    return attemptToolHandled.result;

                var attemptReply   = agentAttempt.answer;
                var attemptService = string.IsNullOrWhiteSpace(agentAttempt.service)
                    ? serviceHint : agentAttempt.service;
                var attemptUrl = agentAttempt.nextStepsUrl ?? "";

                if (!string.IsNullOrWhiteSpace(attemptReply) && !IsGenericOrWeakReply(attemptReply))
                {
                    _memory.SetLastService(sessionId, attemptService);
                    _memory.SetLastIntent(sessionId, detectedIntent);
                    var attemptSuggestions = BuildSuggestions(attemptService, detectedIntent);
                    SaveConversation(sessionId, message, attemptReply, attemptService, detectedIntent, attemptSuggestions);
                    return (attemptReply, attemptService, attemptUrl, bestScore, attemptSuggestions);
                }
            }

            // ── Fallback to clarification ─────────────────────────────────────────
            var clarificationService =
                !string.IsNullOrWhiteSpace(detectedService) && !string.Equals(detectedService, "Unknown", StringComparison.OrdinalIgnoreCase) ? detectedService :
                !string.IsNullOrWhiteSpace(lastService)     && !string.Equals(lastService,     "Unknown", StringComparison.OrdinalIgnoreCase) ? lastService :
                !string.IsNullOrWhiteSpace(serviceHint)     && !string.Equals(serviceHint,     "Unknown", StringComparison.OrdinalIgnoreCase) ? serviceHint :
                "Unknown";

            var clarificationReply       = BuildClarificationReply(clarificationService, lastService, normMsg);
            var clarificationSuggestions = BuildClarificationSuggestions(clarificationService, lastService, normMsg);

            SaveConversation(sessionId, message, clarificationReply, clarificationService, "clarification", clarificationSuggestions);
            return (clarificationReply, clarificationService, "", bestScore, clarificationSuggestions);
        }

        // 13. Strong retrieved service
        var finalService = string.IsNullOrWhiteSpace(bestChunk.Service) ? "Unknown" : bestChunk.Service;
        _memory.SetLastService(sessionId, finalService);
        _memory.SetLastIntent(sessionId, detectedIntent);

        // 14. Let the LangChain agent decide
        var agentResult = await _langChain.RunAgentAsync(message, finalService, context, history);

        var toolHandled = HandleAgentToolResponse(
            sessionId,
            message,
            agentResult,
            bestScore);

        if (toolHandled.hasToolResponse)
            return toolHandled.result;

        var aiReply = agentResult.answer;
        var resolvedService = string.IsNullOrWhiteSpace(agentResult.service) ? finalService : agentResult.service;
        var resolvedNextStepsUrl = string.IsNullOrWhiteSpace(agentResult.nextStepsUrl)
            ? (bestChunk.NextStepsUrl ?? "")
            : agentResult.nextStepsUrl;

        // 15. Fallback to direct OpenAI
        if (string.IsNullOrWhiteSpace(aiReply))
        {
            aiReply = await _openAi.GenerateAnswerAsync(message, finalService, context);
        }

        // 16. Final fallback: if aiReply is missing or still contains generic/internal wording,
        //     replace it with a targeted clarification question rather than a raw chunk or error string.
        if (string.IsNullOrWhiteSpace(aiReply) || IsGenericOrWeakReply(aiReply))
        {
            var fallbackService =
                !string.IsNullOrWhiteSpace(resolvedService) && !string.Equals(resolvedService, "Unknown", StringComparison.OrdinalIgnoreCase) ? resolvedService :
                !string.IsNullOrWhiteSpace(finalService)    && !string.Equals(finalService,    "Unknown", StringComparison.OrdinalIgnoreCase) ? finalService :
                !string.IsNullOrWhiteSpace(lastService)     && !string.Equals(lastService,     "Unknown", StringComparison.OrdinalIgnoreCase) ? lastService :
                "Unknown";
            var fallbackReply       = BuildClarificationReply(fallbackService, lastService, normMsg);
            var fallbackSuggestions = BuildClarificationSuggestions(fallbackService, lastService, normMsg);
            SaveConversation(sessionId, message, fallbackReply, fallbackService, detectedIntent, fallbackSuggestions);
            return (fallbackReply, fallbackService, resolvedNextStepsUrl, bestScore, fallbackSuggestions);
        }

        var finalSuggestions = BuildSuggestions(resolvedService, detectedIntent);

        SaveConversation(sessionId, message, aiReply, resolvedService, detectedIntent, finalSuggestions);

        return (aiReply, resolvedService, resolvedNextStepsUrl, bestScore, finalSuggestions);
    }
    private static bool IsMeaninglessInput(string msg)
{
    if (string.IsNullOrWhiteSpace(msg))
        return true;

    var vague = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "idk", "i dont know", "i don't know", "dont know", "don't know",
        "not sure", "maybe", "umm", "uh", "er", "hmm", "meh",
        "asdf", "asdasd", "sdf", "sdfsadf", "qwerty", "test"
    };

    if (vague.Contains(msg))
        return true;

    if (LooksLikeKeyboardMash(msg))
        return true;

    var words = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // one short unknown token like "asd" or "zzz"
    if (words.Length == 1 &&
    words[0].Length <= 4 &&
    Regex.IsMatch(words[0], @"^[a-z]+$") &&
    !LooksLikeRealWord(words[0]))
{
    return true;
}
    return false;
}

private static bool LooksLikeKeyboardMash(string msg)
{
    if (string.IsNullOrWhiteSpace(msg))
        return false;

    var compact = msg.Replace(" ", "");

    // repeated same char: zzzz, aaaaa
    if (compact.Length >= 4 && compact.All(c => c == compact[0]))
        return true;

    // random-looking letters with almost no vowels
    if (compact.Length >= 6 && Regex.IsMatch(compact, @"^[a-z]+$"))
    {
        var vowelCount = compact.Count(c => "aeiou".Contains(c));
        if (vowelCount <= 1)
            return true;
    }

    return false;
}

private static bool LooksLikeRealWord(string text)
{
    if (string.IsNullOrWhiteSpace(text))
        return false;

    return text.Contains("hi") ||
           text.Contains("hey") ||
           text.Contains("bye") ||
           text.Contains("tax") ||
           text.Contains("bin") ||
           text.Contains("help") ||
           text.Contains("school") ||
           text.Contains("plan") ||
           text.Contains("rent") ||
           text.Contains("bill") ||
           text.Contains("home") ||
           text.Contains("park");
}


    private (bool hasToolResponse, (string reply, string service, string nextStepsUrl, float score, List<string> suggestions) result)
        HandleAgentToolResponse(
            string sessionId,
            string userMessage,
            (string answer, string service, string action, bool needsClarification, string toolUsed, string nextStepsUrl) agentResult,
            float score)
    {
        if (!string.Equals(agentResult.action, "tool", StringComparison.OrdinalIgnoreCase))
            return (false, default);

        if (string.Equals(agentResult.toolUsed, "postcode_lookup", StringComparison.OrdinalIgnoreCase))
        {
            var postcode = agentResult.answer
                .Replace("POSTCODE_LOOKUP::", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            _memory.SetMaskedPostcode(sessionId, postcode);
            _memory.SetPendingFlow(sessionId, "postcode_lookup_started");

            var reply = $"POSTCODE_LOOKUP::{postcode}";
            var service = string.IsNullOrWhiteSpace(agentResult.service) ? "Waste & Bins" : agentResult.service;
            var toolSuggestions = new List<string>();

            SaveConversation(sessionId, userMessage, reply, service, "postcode_lookup", toolSuggestions);
            return (true, (reply, service, "", score, toolSuggestions));
        }

        if (!string.IsNullOrWhiteSpace(agentResult.answer))
        {
            var service = string.IsNullOrWhiteSpace(agentResult.service) ? "Unknown" : agentResult.service;
            var toolSuggestions = BuildSuggestions(service, _memory.GetLastIntent(sessionId));

            SaveConversation(sessionId, userMessage, agentResult.answer, service, _memory.GetLastIntent(sessionId), toolSuggestions);
            return (true, (agentResult.answer, service, agentResult.nextStepsUrl ?? "", score, toolSuggestions));
        }

        return (false, default);
    }

    private void SaveConversation(string sessionId, string userMessage, string assistantReply, string service, string intent, List<string> suggestions)
    {
        _memory.AddTurn(sessionId, "user", userMessage);
        _memory.AddTurn(sessionId, "assistant", assistantReply);
        _memory.SetLastService(sessionId, service);
        _memory.SetLastIntent(sessionId, intent);
        _memory.SetLastSuggestions(sessionId, suggestions);
    }

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "";

        input = input.ToLowerInvariant();
        input = input.Replace("badg", "badge");
        input = input.Replace("disabl", "disabled");
        input = input.Replace("bin day", "bin collection");
        input = input.Replace("c tax", "council tax");

        var chars = input.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        return string.Join(" ", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string DetectService(string normMsg, Dictionary<string, string[]> triggers)
    {
        foreach (var kv in triggers)
        {
            foreach (var trigger in kv.Value)
            {
                var normalizedTrigger = Normalize(trigger);
                if (!string.IsNullOrWhiteSpace(normalizedTrigger) && normMsg.Contains(normalizedTrigger))
                    return kv.Key;
            }
        }

        return "";
    }

    private static string DetectIntent(string normMsg, string service)
{
    if (string.IsNullOrWhiteSpace(normMsg))
        return "";

    if (normMsg.Contains("apply"))
        return "apply";

    if (normMsg.Contains("eligible") || normMsg.Contains("eligibility") || normMsg.Contains("qualify"))
        return "eligibility";

    if (normMsg.Contains("pay") || normMsg.Contains("payment") || normMsg.Contains("balance"))
        return "payment";

    if (normMsg.Contains("missed bin"))
        return "missed_bin";

    if (normMsg.Contains("new bin") || normMsg.Contains("replacement bin"))
        return "new_bin";

    if (normMsg.Contains("collection"))
        return "collection";

    if (normMsg.Contains("planning application") || normMsg.Contains("planning permission"))
        return "planning";

    if (normMsg.Contains("library") || normMsg.Contains("renew books") || normMsg.Contains("e-books"))
        return "library";

    if (normMsg.Contains("housing") || normMsg.Contains("homeless"))
        return "housing";

    if (normMsg.Contains("contact") || normMsg.Contains("phone number") || normMsg.Contains("email"))
        return "contact";

    return service?.ToLowerInvariant() ?? "";
}

    private static bool IsBinCollectionDayIntent(string normMsg)
    {
        if (string.IsNullOrWhiteSpace(normMsg))
            return false;

        return normMsg.Contains("bin collection day") ||
               normMsg.Contains("bin day") ||
               normMsg.Contains("collection day") ||
               normMsg.Contains("waste collection") ||
               normMsg.Contains("recycling collection") ||
               (normMsg.Contains("bin") && normMsg.Contains("day")) ||
               (normMsg.Contains("recycling") && normMsg.Contains("day"));
    }

    // private static bool IsWasteFollowUpIntent(string normMsg)
    // {
    //     if (string.IsNullOrWhiteSpace(normMsg))
    //         return false;

    //     return normMsg.Contains("bin") ||
    //            normMsg.Contains("bins") ||
    //            normMsg.Contains("waste") ||
    //            normMsg.Contains("recycling") ||
    //            normMsg.Contains("address") ||
    //            normMsg.Contains("same address") ||
    //            normMsg.Contains("different address");
    // }
    private static bool IsWasteFollowUpIntent(string normMsg)
{
    if (string.IsNullOrWhiteSpace(normMsg))
        return false;

    return normMsg.Contains("same address") ||
           normMsg.Contains("different address") ||
           normMsg.Contains("same postcode") ||
           normMsg.Contains("previous address") ||
           normMsg.Contains("address i told you before") ||
           normMsg.Contains("previously selected address") ||
           normMsg.Contains("previous postcode");
}

    private static bool IsShortFollowUp(string normMsg)
    {
        if (string.IsNullOrWhiteSpace(normMsg))
            return false;

        return normMsg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5;
    }

    private static bool IsLikelyContinuation(string normMsg)
    {
        if (string.IsNullOrWhiteSpace(normMsg))
            return false;

        return IsShortFollowUp(normMsg) ||
               normMsg.Contains("how do i apply") ||
               normMsg.Contains("contact details") ||
               normMsg.Contains("what do i need") ||
               normMsg.Contains("am i eligible") ||
               normMsg.Contains("how much") ||
               normMsg.Contains("what next");
    }

    private static bool LooksLikeUkPostcode(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var cleaned = input.Trim().ToUpperInvariant();
        return Regex.IsMatch(cleaned, @"^[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}$");
    }

    /// <summary>
    /// Returns true when a reply is empty, exposes internal retrieval wording,
    /// or is too vague to be useful — any of these should trigger a clarification.
    /// </summary>
    private static bool IsGenericOrWeakReply(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return true;
        return reply.Contains("I'm not sure",             StringComparison.OrdinalIgnoreCase) ||
               reply.Contains("not configured",           StringComparison.OrdinalIgnoreCase) ||
               reply.Contains("couldn't find",            StringComparison.OrdinalIgnoreCase) ||
               reply.Contains("could not find",           StringComparison.OrdinalIgnoreCase) ||
               reply.Contains("context does not provide", StringComparison.OrdinalIgnoreCase) ||
               reply.Contains("no specific information",  StringComparison.OrdinalIgnoreCase) ||
               reply.Contains("reliable answer",          StringComparison.OrdinalIgnoreCase) ||
               reply.Contains("not able to find",         StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns a short, service- and intent-specific clarification question.
    /// Service priority: serviceHint → lastService → generic fallback.
    /// </summary>
    private static string BuildClarificationReply(string serviceHint, string lastService, string normMsg)
    {
        var svc = !string.IsNullOrWhiteSpace(serviceHint) && !string.Equals(serviceHint, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? serviceHint
            : !string.IsNullOrWhiteSpace(lastService) && !string.Equals(lastService, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? lastService
            : "Unknown";

        if (svc == "Council Tax")
        {
            if (normMsg.Contains("payment") || normMsg.Contains("pay"))
                return "I can help with Council Tax payments. Do you want to pay online, set up a direct debit, or get help because you're struggling to pay?";
            if (normMsg.Contains("support") || normMsg.Contains("struggling") || normMsg.Contains("money") || normMsg.Contains("low income"))
                return "If you're struggling with Council Tax, I can help with support options. Do you want to check whether you might qualify for Council Tax Support, or do you need help with arrears or a payment arrangement?";
            if (normMsg.Contains("discount") || normMsg.Contains("exemption"))
                return "I can help with Council Tax discounts and exemptions. Are you asking about single person discount, student exemption, or another reduction?";
            return "I can help with Council Tax. Are you asking about your balance, making a payment, getting a discount, or support with the bill?";
        }

        if (svc == "Benefits & Support")
        {
            if (normMsg.Contains("eligible") || normMsg.Contains("eligibility") || normMsg.Contains("qualify"))
                return "I can help check support eligibility. Are you asking about Council Tax Support, Housing Benefit, hardship support, or benefits in general?";
            if (normMsg.Contains("apply"))
                return "I can help with applying for support. Are you trying to apply for Council Tax Support, Housing Benefit, or another kind of help?";
            if (normMsg.Contains("evidence") || normMsg.Contains("document") || normMsg.Contains("proof"))
                return "I can help with evidence requirements. Are you asking what documents you need for Council Tax Support, Housing Benefit, or another application?";
            return "I can help with benefits and support. Are you asking about eligibility, applying, emergency help, or documents you need?";
        }

        return svc switch
        {
            "Waste & Bins" => "I can help with bins and waste. Is this about your collection day, a missed bin, or requesting a new one?",
            "Education"    => "I can help with schools. Are you asking about admissions, the application deadline, in-year transfers, or finding a school nearby?",
            "Housing"      => "I can help with housing. Are you asking about homelessness, finding a home, housing support, or property repairs?",
            "Planning"     => "I can help with planning. Are you looking to check an application, apply for permission, or comment on a proposal?",
            "Libraries"    => "I can help with library services. Are you asking about renewing books, joining, e-books, or fines?",
            "Contact Us"   => "You can reach Bradford Council by phone on 01274 431000, online at bradford.gov.uk, or in person at City Hall, Bradford, BD1 1HY. What specifically did you need help with?",
            _              => "I didn't quite catch what you need. Could you tell me a bit more? For example, is this about Council Tax, bins, benefits, housing, or schools?"
        };
    }

    /// <summary>
    /// Returns targeted suggestion chips to accompany a clarification reply.
    /// Intent-aware for Council Tax and Benefits &amp; Support; falls back to general chips otherwise.
    /// </summary>
    private static List<string> BuildClarificationSuggestions(string serviceHint, string lastService, string normMsg)
    {
        var svc = !string.IsNullOrWhiteSpace(serviceHint) && !string.Equals(serviceHint, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? serviceHint
            : !string.IsNullOrWhiteSpace(lastService) && !string.Equals(lastService, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? lastService
            : "Unknown";

        if (svc == "Council Tax")
        {
            if (normMsg.Contains("payment") || normMsg.Contains("pay"))
                return new List<string>
                {
                    "How do I pay my Council Tax online?",
                    "How do I set up a Council Tax direct debit?",
                    "I am struggling to pay my Council Tax",
                    "How do I check my Council Tax balance?"
                };
            if (normMsg.Contains("support") || normMsg.Contains("struggling") || normMsg.Contains("money"))
                return new List<string>
                {
                    "How do I apply for Council Tax Support?",
                    "How do I set up a Council Tax payment arrangement?",
                    "I am in Council Tax arrears — what can I do?",
                    "Am I eligible for Council Tax Support?"
                };
            return new List<string>
            {
                "How do I check my Council Tax balance?",
                "How do I pay my Council Tax?",
                "Can I get a Council Tax discount?",
                "How do I apply for Council Tax Support?"
            };
        }

        if (svc == "Benefits & Support")
        {
            if (normMsg.Contains("eligible") || normMsg.Contains("qualify"))
                return new List<string>
                {
                    "Am I eligible for Council Tax Support?",
                    "How do I qualify for Housing Benefit?",
                    "What hardship support is available?",
                    "How do I check what benefits I can get?"
                };
            if (normMsg.Contains("apply"))
                return new List<string>
                {
                    "How do I apply for Council Tax Support?",
                    "How do I apply for Housing Benefit?",
                    "How do I get emergency financial help?",
                    "What documents do I need to apply for benefits?"
                };
            return new List<string>
            {
                "Am I eligible for Council Tax Support?",
                "How do I apply for Housing Benefit?",
                "What evidence do I need for a benefits application?",
                "How do I get emergency financial support?"
            };
        }

        return svc switch
        {
            "Waste & Bins" => new List<string>
            {
                "When is my bin collection day?",
                "How do I report a missed bin?",
                "How do I request a new bin?"
            },
            "Education" => new List<string>
            {
                "How do I apply for a school place?",
                "What is the school admissions deadline?",
                "How do in-year school transfers work?",
                "How do I find schools near me?"
            },
            "Housing" => new List<string>
            {
                "How do I get emergency homelessness help?",
                "How do I apply for housing support?",
                "How do I report a repair needed on my home?",
                "How do I find a home in Bradford?"
            },
            "Planning" => new List<string>
            {
                "How do I check my planning application status?",
                "How do I apply for planning permission?",
                "How do I comment on a planning application?"
            },
            "Libraries" => new List<string>
            {
                "How do I renew my library books online?",
                "How do I join Bradford libraries?",
                "How do I borrow e-books from the library?",
                "How do I pay a library fine?"
            },
            _ => new List<string>
            {
                "How do I pay my Council Tax?",
                "When is my bin collection?",
                "How do I apply for benefits?",
                "How do I get housing help?"
            }
        };
    }

    private static List<string> BuildSuggestions(string service, string intent)
    {
        var svc = service?.Trim() ?? "";
        var suggestions = new List<string>();

        switch (svc)
        {
            case "Council Tax":
                suggestions.Add("How do I pay my Council Tax?");
                suggestions.Add("Can I get a Council Tax discount?");
                suggestions.Add("I have moved home");
                break;

            case "Waste & Bins":
                suggestions.Add("When is my bin collection?");
                suggestions.Add("Report a missed bin");
                suggestions.Add("Request a new bin");
                break;

            case "Benefits & Support":
                suggestions.Add("How do I apply?");
                suggestions.Add("Am I eligible?");
                suggestions.Add("What evidence do I need?");
                break;

            case "Education":
                suggestions.Add("How do I apply for a school place?");
                suggestions.Add("What is the deadline?");
                suggestions.Add("How do in-year transfers work?");
                break;

            
            case "Planning":
                suggestions.Add("How can I check my planning application status?");
                suggestions.Add("View planning applications");
                suggestions.Add("How do I apply for planning permission?");
                break;

            case "Libraries":
                suggestions.Add("How do I renew library books online?");
                suggestions.Add("Can I borrow e-books?");
                suggestions.Add("How do I join the library?");
                break;

            case "Housing":
                suggestions.Add("How do I get housing support?");
                suggestions.Add("I am homeless");
                suggestions.Add("How can I find a home?");
                break;

            case "Contact Us":
                suggestions.Add("How can I contact the council?");
                suggestions.Add("What is the council phone number?");
                suggestions.Add("How do I sign up for email alerts?");
                break;

            case "Appointment":
                suggestions.Add("Book an appointment");
                suggestions.Add("Council Tax enquiry");
                suggestions.Add("Housing advice");
                suggestions.Add("Benefits & Support");
                break;

            case "Location":
                suggestions.Add("Find nearest library");
                suggestions.Add("Find council office");
                suggestions.Add("Find recycling centre");
                suggestions.Add("Find nearby schools");
                break;

            case "Form Assistant":
                suggestions.Add("Benefits form");
                suggestions.Add("Housing form");
                suggestions.Add("School application");
                suggestions.Add("Blue Badge form");
                break;

            default:
                suggestions.Add("Council Tax");
                suggestions.Add("Waste & Bins");
                suggestions.Add("Benefits & Support");
                suggestions.Add("Planning");
                break;
        }

        if (svc == "Waste & Bins" && intent == "collection")
        {
            suggestions.Insert(0, "Use same address");
            suggestions.Insert(1, "Use different address");
        }

        return suggestions.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
    }
    private static bool IsSameAddressIntent(string msg)
{
    if (string.IsNullOrWhiteSpace(msg))
        return false;

    return msg.Contains("same address");
}

private static bool IsDifferentAddressIntent(string msg)
{
    if (string.IsNullOrWhiteSpace(msg))
        return false;

    return msg.Contains("different address");
}
    private static bool IsBinFollowUpIntent(string msg)
{
    if (string.IsNullOrWhiteSpace(msg))
        return false;

    return msg.Contains("general waste") ||
           msg.Contains("recycling") ||
           msg.Contains("garden waste") ||
           msg.Contains("what about garden waste") ||
           msg.Contains("what about recycling") ||
           msg.Contains("what about general waste") ||
           msg.Contains("tell me about the bin collection") ||
           msg.Contains("bin collection of the address i told you before") ||
           msg.Contains("address i told you before") ||
           msg.Contains("previously selected address");
}

    private static string BuildBinFollowUpReply(string normMsg, string activeAddress, string lastBinResult)
    {
        if (string.IsNullOrWhiteSpace(lastBinResult))
            return "I could not find the previous bin collection details for that address.";

        if (normMsg.Contains("general waste"))
        {
            var section = ExtractSection(lastBinResult, "General waste:");
            return string.IsNullOrWhiteSpace(section)
                ? $"For your previously selected address, here are the bin collection details:\n\n{lastBinResult}"
                : $"For your previously selected address, the general waste collection details are:\n\n{section}";
        }

        if (normMsg.Contains("recycling"))
        {
            var section = ExtractSection(lastBinResult, "Recycling waste:");
            return string.IsNullOrWhiteSpace(section)
                ? $"For your previously selected address, here are the bin collection details:\n\n{lastBinResult}"
                : $"For your previously selected address, the recycling collection details are:\n\n{section}";
        }

        if (normMsg.Contains("garden waste"))
        {
            var section = ExtractGardenWasteSection(lastBinResult);
            return string.IsNullOrWhiteSpace(section)
                ? $"For your previously selected address, here are the bin collection details:\n\n{lastBinResult}"
                : $"For your previously selected address, the garden waste details are:\n\n{section}";
        }

        return $"For your previously selected address, here are the bin collection details:\n\n{lastBinResult}";
    }

    private static string ExtractSection(string text, string heading)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .ToList();

        var start = lines.FindIndex(x => x.StartsWith(heading, StringComparison.OrdinalIgnoreCase));
        if (start < 0)
            return "";

        var collected = new List<string> { lines[start] };

        for (int i = start + 1; i < lines.Count; i++)
        {
            var line = lines[i];

            if (!line.StartsWith("-") &&
                line.EndsWith(":") &&
                !line.StartsWith(heading, StringComparison.OrdinalIgnoreCase))
                break;

            if (line.StartsWith("Garden waste subscription", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Garden waste:", StringComparison.OrdinalIgnoreCase))
                break;

            collected.Add(line);
        }

        return string.Join("\n", collected);
    }

    private static string ExtractGardenWasteSection(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .ToList();

        var collected = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("Garden waste subscription", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Garden waste:", StringComparison.OrdinalIgnoreCase))
            {
                collected.Add(line);
            }
        }

        return string.Join("\n", collected);
    }

    // ── Crisis / self-harm / suicide detection ────────────────────────────────────

    /// <summary>
    /// Returns true when the message contains clear suicide intent, self-harm language,
    /// or severe hopelessness phrases.  Checked before ALL other guards and retrieval
    /// so the response is never contaminated by service context.
    /// </summary>
    private static bool IsCrisisIntent(string normMsg)
    {
        if (string.IsNullOrWhiteSpace(normMsg)) return false;

        // ── Explicit suicide / self-harm intent ───────────────────────────────────
        var directPhrases = new[]
        {
            "commit suicide",
            "want to die",
            "kill myself", "kill my self",
            "hurt myself", "hurt my self",
            "harm myself", "harm my self",
            "self harm", "self-harm", "selfharm",
            "end my life", "ending my life", "end it all",
            "take my life", "taking my life",
            "want to disappear forever",
            "going to self harm",
            "going to kill myself",
            "going to end my life",
            "plan to kill myself",
            "want to kill myself",
            "want to hurt myself",
            "feel like ending my life",
            "i feel like ending",
            "no reason to live",
            "not worth living",
            "life is not worth",
            "want to be dead",
            "better off dead",
            "better off without me",
            "suicide",
            "suicidal",
        };

        foreach (var phrase in directPhrases)
            if (normMsg.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return true;

        // ── Severe hopelessness / crisis phrases ──────────────────────────────────
        // These are more contextual and only trigger on strong, specific constructions
        // to minimise false positives on everyday speech.
        var hopelessPhrases = new[]
        {
            "i cannot go on",
            "i can't go on",
            "i cant go on",
            "i feel hopeless",
            "feeling hopeless",
            "completely hopeless",
            "i am depressed",
            "i feel depressed",
            "i don't want to be here anymore",
            "i dont want to be here anymore",
            "i do not want to be here anymore",
            "i want to disappear",
            "no point in living",
            "no point going on",
            "make it all stop",
            "i cannot cope anymore",
            "i can't cope anymore",
            "i cant cope anymore",
            "i am going to self harm",
            "i want to self harm",
        };

        foreach (var phrase in hopelessPhrases)
            if (normMsg.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    // ── Small-talk detection and response ────────────────────────────────────────

    /// <summary>
    /// Returns true for greetings, farewells, thanks, and light social phrases
    /// that should be answered immediately, before any embedding or retrieval.
    /// </summary>
    private static bool IsSmallTalk(string normMsg)
    {
        if (string.IsNullOrWhiteSpace(normMsg)) return false;

        // Short exact phrases
        var exactPhrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Greetings
            "hi", "hey", "hello", "hiya", "yo", "howdy", "alright", "alright mate",
            // Openers
            "help", "ok", "okay", "please",
            // Thanks
            "thanks", "thank you", "thank you very much", "thanks a lot", "many thanks",
            "cheers", "ta", "appreciated",
            // Positive acknowledgements
            "great", "brilliant", "perfect", "nice", "good", "sounds good", "awesome",
            "excellent", "lovely", "wonderful", "fab", "fabulous", "ace",
            // Farewells
            "bye", "bye bye", "goodbye", "good bye", "see you", "see ya", "cya",
            "take care", "cheerio", "ta ta", "tata", "later", "laters",
            // Time-of-day greetings
            "good morning", "good afternoon", "good evening", "good night",
            "morning", "afternoon", "evening",
            // How are you
            "how are you", "how are you doing", "how are things", "how is it going",
            "how are you today", "how r u"
        };

        if (exactPhrases.Contains(normMsg)) return true;

        var words = normMsg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Short greetings with a name or punctuation: "hi there", "hey there", "hello there"
        if ((normMsg.StartsWith("hi ") || normMsg.StartsWith("hey ") || normMsg.StartsWith("hello ") || normMsg.StartsWith("hiya ")) && words.Length <= 4)
            return true;

        // Time-of-day greetings with trailing words: "good morning everyone", "good afternoon!"
        if ((normMsg.StartsWith("good morning") || normMsg.StartsWith("good afternoon") || normMsg.StartsWith("good evening")) && words.Length <= 5)
            return true;

        // Thanks phrases up to ~6 words
        if ((normMsg.Contains("thank you") || normMsg.StartsWith("thanks")) && words.Length <= 6)
            return true;

        // Farewell phrases
        if ((normMsg.Contains("bye") || normMsg.Contains("goodbye") || normMsg.Contains("see you") || normMsg.Contains("take care") || normMsg.Contains("cheerio")) && words.Length <= 6)
            return true;

        // How are you variants
        if (normMsg.Contains("how are you") || normMsg.Contains("how are things") || normMsg.Contains("how is it going"))
            return true;

        return false;
    }

    /// <summary>
    /// Returns a short, natural, varied small-talk reply.
    /// Replies stay in the Bradford Council assistant persona and always
    /// invite the user to ask a council question.
    /// </summary>
    private static string GetSmallTalkReply(string normMsg)
    {
        var hour = DateTime.Now.Hour;
        var timeOfDay = hour < 12 ? "morning" : hour < 17 ? "afternoon" : "evening";

        // ── Farewells ───────────────────────────────────────────────────────────
        if (normMsg.Contains("bye") || normMsg.Contains("goodbye") ||
            normMsg.Contains("see you") || normMsg.Contains("see ya") ||
            normMsg.Contains("take care") || normMsg.Contains("cheerio") ||
            normMsg.Contains("ta ta") || normMsg.Contains("tata"))
        {
            var farewells = new[]
            {
                $"Goodbye! Don't hesitate to come back if you need help with any council services.",
                $"Take care! I'm here any time you need help.",
                $"Thanks for getting in touch. Have a great {timeOfDay}!",
                $"Bye for now — feel free to return if you have any other questions."
            };
            return farewells[PickVariant(normMsg, farewells.Length)];
        }

        // ── Thanks / positive acknowledgements ─────────────────────────────────
        if (normMsg.Contains("thank") || normMsg == "cheers" || normMsg == "ta" ||
            normMsg == "brilliant" || normMsg == "perfect" || normMsg == "great" ||
            normMsg == "nice" || normMsg == "sounds good" || normMsg == "awesome" ||
            normMsg == "excellent" || normMsg == "lovely" || normMsg == "fab" ||
            normMsg == "fabulous" || normMsg == "ace" || normMsg == "appreciated")
        {
            var thanks = new[]
            {
                "You're welcome! Is there anything else I can help you with?",
                "Happy to help! Let me know if you have any other questions.",
                "No problem at all. Is there anything else you'd like to know?",
                "Glad I could help — feel free to ask if you need anything else."
            };
            return thanks[PickVariant(normMsg, thanks.Length)];
        }

        // ── How are you ─────────────────────────────────────────────────────────
        if (normMsg.Contains("how are you") || normMsg.Contains("how are things") || normMsg.Contains("how is it going"))
            return $"Thanks for asking! I'm here and ready to help. What can I do for you today?";

        // ── Time-of-day greetings ───────────────────────────────────────────────
        if (normMsg.StartsWith("good morning") || normMsg == "morning")
            return "Good morning! How can I help you with Bradford Council services today?";

        if (normMsg.StartsWith("good afternoon") || normMsg == "afternoon")
            return "Good afternoon! How can I help you today?";

        if (normMsg.StartsWith("good evening") || normMsg == "evening")
            return "Good evening! How can I help you with Bradford Council services?";

        if (normMsg == "good night")
            return "Good night! If you need any council services, I'm here any time.";

        // ── Generic greetings / "help" / openers ────────────────────────────────
        var greetings = new[]
        {
            $"Good {timeOfDay}! I can help with Council Tax, bins and waste, benefits, housing, schools, and more. What would you like to know?",
            $"Hello! I'm the Bradford Council assistant. How can I help you today?",
            $"Hi there! I'm here to help with Bradford Council services — what can I do for you?",
            $"Hello! I can help with Council Tax, bin collections, benefits, and much more. What do you need?"
        };
        return greetings[PickVariant(normMsg, greetings.Length)];
    }

    /// <summary>
    /// Picks a deterministic variant index based on the message text.
    /// This avoids true randomness (which would vary per compile) while still
    /// spreading responses across the available options.
    /// </summary>
    private static int PickVariant(string msg, int count)
        => count <= 1 ? 0 : Math.Abs(msg.GetHashCode()) % count;

    // ── Context reset ─────────────────────────────────────────────────────────

    private static bool IsContextResetIntent(string msg)
{
    if (string.IsNullOrWhiteSpace(msg))
        return false;

    return msg.Contains("something else") ||
           msg.Contains("ask something else") ||
           msg.Contains("different question") ||
           msg.Contains("another question") ||
           msg.Contains("different topic");
}

    private static bool IsContactUsIntent(string normMsg)
    {
        if (string.IsNullOrWhiteSpace(normMsg)) return false;
        return normMsg.Contains("contact the council") ||
               normMsg.Contains("contact us") ||
               normMsg.Contains("phone number") ||
               normMsg.Contains("telephone number") ||
               normMsg.Contains("council number") ||
               normMsg.Contains("call the council") ||
               normMsg.Contains("contact details") ||
               normMsg.Contains("opening hours") ||
               normMsg.Contains("council office") && (normMsg.Contains("where") || normMsg.Contains("visit") || normMsg.Contains("find")) ||
               normMsg.Contains("make a complaint") ||
               normMsg.Contains("how to complain") ||
               normMsg.Contains("give feedback") ||
               normMsg.Contains("email the council") ||
               normMsg.Contains("council email") ||
               normMsg.Contains("council address") ||
               normMsg.Contains("post to the council") ||
               normMsg.Contains("write to the council") ||
               normMsg.Contains("sign up for email alerts") ||
               normMsg.Contains("email alerts") ||
               normMsg.Contains("social media") && normMsg.Contains("council") ||
               normMsg.Contains("emergency contact") ||
               normMsg.Contains("customer service number") ||
               normMsg.Contains("customer services");
    }

    private static string GetContactUsReply(string normMsg)
    {
        // Complaint / feedback
        if (normMsg.Contains("complaint") || normMsg.Contains("complain") || normMsg.Contains("feedback"))
            return "To make a complaint or give feedback to Bradford Council, you can use the online complaints form on the Bradford Council website, write to them, or visit a Customer Service Centre in person. For general feedback you can also call 01274 431000.";

        // Opening hours
        if (normMsg.Contains("opening hours") || normMsg.Contains("open"))
            return "Bradford Council Customer Service Centres are generally open Monday to Thursday 8:30am–5:00pm and Friday 8:30am–4:30pm. Hours may vary by location. You can check specific hours at bradford.gov.uk/contact-us.";

        // Visit / find office
        if (normMsg.Contains("visit") || normMsg.Contains("in person") || normMsg.Contains("office") || normMsg.Contains("address"))
            return "You can visit Bradford Council in person at City Hall, Centenary Square, Bradford, BD1 1HY. For other Customer Service Centre locations, visit bradford.gov.uk/contact-us.";

        // Email alerts / updates
        if (normMsg.Contains("email alert") || normMsg.Contains("email update") || normMsg.Contains("sign up") || normMsg.Contains("updates"))
            return "You can sign up for email alerts and updates from Bradford Council on the Bradford Council website. Go to bradford.gov.uk and look for the 'Sign up for email alerts' option.";

        // Social media
        if (normMsg.Contains("social media") || normMsg.Contains("twitter") || normMsg.Contains("facebook"))
            return "Bradford Council is on social media. You can find them on X (formerly Twitter) @bradfordmdc and on Facebook at facebook.com/bradfordmdc. For official enquiries, it's best to contact them directly rather than through social media.";

        // Emergency
        if (normMsg.Contains("emergency"))
            return "For council emergencies outside office hours, call 01274 431000. For housing emergencies call the 24-hour homeless line on 01274 435999. For life-threatening emergencies always call 999.";

        // Post / documents
        if (normMsg.Contains("post") || normMsg.Contains("send documents") || normMsg.Contains("write") || normMsg.Contains("letter"))
            return "You can write to Bradford Council at City Hall, Centenary Square, Bradford, BD1 1HY. For specific departments, addresses are listed on each service page at bradford.gov.uk.";

        // Default — main contact details
        return "You can contact Bradford Council by:\n\n" +
               "📞 **Phone:** 01274 431000 (Monday–Thursday 8:30am–5pm, Friday 8:30am–4:30pm)\n" +
               "🌐 **Website:** bradford.gov.uk\n" +
               "📍 **In person:** City Hall, Centenary Square, Bradford, BD1 1HY\n\n" +
               "For specific services, each team has its own contact details on the website.";
    }

    private static bool IsPlanningApplicationStatusIntent(string normMsg)
    {
        if (string.IsNullOrWhiteSpace(normMsg)) return false;
        return (normMsg.Contains("check") && normMsg.Contains("planning")) ||
               (normMsg.Contains("planning application") && (normMsg.Contains("status") || normMsg.Contains("progress") || normMsg.Contains("update") || normMsg.Contains("view") || normMsg.Contains("look up") || normMsg.Contains("find"))) ||
               normMsg.Contains("planning application status") ||
               normMsg.Contains("status of my planning") ||
               normMsg.Contains("track planning") ||
               normMsg.Contains("view planning application") ||
               normMsg.Contains("search planning application");
    }

    // Words that, if present after "I am / I'm", indicate the message describes a
    // situation rather than a name — and must NOT be caught by the identity guard.
    private static readonly HashSet<string> _situationWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "at", "in", "of", "with", "not", "unable", "trying", "going", "having",
        "getting", "risk", "worried", "scared", "afraid", "struggling", "behind",
        "facing", "experiencing", "a", "an", "the", "no", "without", "here",
        "there", "looking", "seeking", "desperate", "evicted", "homeless",
        "unemployed", "disabled", "ill", "sick", "worried", "concerned"
    };

    private static bool IsIdentityOnlyInput(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return false;

        // Must start with an identity prefix
        var match = Regex.Match(msg.Trim(),
            @"^(i am|i'm|my name is)\s+(.+?)(\s*[.,]?\s*(i want help|i need help|help|hi|hello|hey|please))?$",
            RegexOptions.IgnoreCase);
        if (!match.Success) return false;

        var namePart = match.Groups[2].Value.Trim();
        var nameWords = namePart.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // A name is 1–3 words. More words = a sentence about a situation.
        if (nameWords.Length > 3) return false;

        // If the first word after "I am" is a situation/circumstance word, it's not a name
        if (nameWords.Length > 0 && _situationWords.Contains(nameWords[0])) return false;

        return true;
    }

    // If a message contains any of these, it has a concrete service signal and should
    // NOT be treated as vague — even if it also contains "help" or "support".
    private static readonly HashSet<string> _serviceNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "council tax", "tax", "bill", "bins", "bin", "waste", "recycling",
        "benefits", "benefit", "housing benefit", "rent", "mortgage",
        "housing", "homeless", "eviction", "school", "admissions", "education",
        "planning", "library", "libraries", "blue badge", "disabled", "disability",
        "universal credit", "job", "jobless", "unemployed", "money", "bills",
        "payment", "debt", "arrears", "hardship", "food bank", "cost of living"
    };

    /// <summary>
    /// Returns true for messages that express a desire for help but contain no service signal.
    /// These should be answered with a service-menu prompt, never sent to embedding/retrieval.
    /// </summary>
    private static bool IsVagueHelpRequest(string normMsg)
    {
        if (string.IsNullOrWhiteSpace(normMsg)) return false;

        // If the message contains any concrete service noun, it is NOT vague — let it route
        if (_serviceNouns.Any(n => normMsg.Contains(n, StringComparison.OrdinalIgnoreCase)))
            return false;

        var exactMatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "i want help", "i need help", "help me", "can you help me", "can you help",
            "i need support", "i want support", "i need assistance", "i want assistance",
            "i need advice", "i want advice", "help me please", "please help me",
            "i need some help", "i want some help", "i could use some help",
            "how can you help me", "what can you help with", "what can you do",
            "what do you do", "what can i ask you", "what can i ask"
        };

        if (exactMatches.Contains(normMsg)) return true;

        // Short messages that are just "help" variants with no service noun
        var words = normMsg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 5 &&
            (normMsg.StartsWith("i want help") || normMsg.StartsWith("i need help") ||
             normMsg.StartsWith("help me") || normMsg.StartsWith("please help")))
            return true;

        return false;
    }

private static bool IsMissedBinIntent(string msg)
{
    if (string.IsNullOrWhiteSpace(msg))
        return false;

    return msg.Contains("missed bin") ||
           msg.Contains("bin was not collected") ||
           msg.Contains("my bin was not collected") ||
           msg.Contains("report a missed bin") ||
           msg.Contains("missed collection");
}
    private static bool IsNewBinRequestIntent(string msg)
{
    if (string.IsNullOrWhiteSpace(msg))
        return false;

    return msg.Contains("new bin") ||
           msg.Contains("replacement bin") ||
           msg.Contains("bin cost") ||
           msg.Contains("cost of a new bin") ||
           msg.Contains("how much does the new bin cost") ||
           msg.Contains("how much does a new bin cost") ||
           msg.Contains("request a new bin") ||
           msg.Contains("get new wheeled bins") ||
           msg.Contains("recycling containers") ||
           msg.Contains("new recycle bin") ||
           msg.Contains("new recycling bin") ||
           msg.Contains("replacement recycling container") ||
           msg.Contains("replacement container");
}
private static bool IsEnterPostcodeIntent(string normMsg)
{
    return normMsg == "enter postcode" || normMsg == "postcode";
}

    // ── New service intent helpers ────────────────────────────────────────────────

    private static bool IsLocationLookupIntent(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return false;
        return msg.Contains("nearest") ||
               msg.Contains("near me") ||
               msg.Contains("find a library") ||
               msg.Contains("find library") ||
               msg.Contains("find a council office") ||
               msg.Contains("find council office") ||
               msg.Contains("find a recycling centre") ||
               msg.Contains("recycling centre near") ||
               msg.Contains("library near") ||
               msg.Contains("closest") && (msg.Contains("library") || msg.Contains("office") || msg.Contains("recycling")) ||
               msg.Contains("where is my nearest");
    }

    private static string DetectLocationSubType(string msg)
    {
        if (msg.Contains("library"))          return "library";
        if (msg.Contains("recycling"))        return "recycling_centre";
        if (msg.Contains("council office") ||
            msg.Contains("office"))           return "council_office";
        if (msg.Contains("school"))           return "school";
        return "all";
    }

    private static bool IsAppointmentIntent(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return false;
        return msg.Contains("book an appointment") ||
               msg.Contains("book appointment") ||
               msg.Contains("make an appointment") ||
               msg.Contains("schedule a call") ||
               msg.Contains("arrange a visit") ||
               msg.Contains("book a call") ||
               msg.Contains("callback") ||
               msg.Contains("call back") &&
                   (msg.Contains("from") || msg.Contains("council") || msg.Contains("arrange")) ||
               msg.Contains("reschedule appointment") ||
               msg.Contains("speak to someone at the council") ||
               msg.Contains("talk to someone at bradford");
    }

    /// <summary>
    /// Returns true when the user is mid-appointment-flow but their message is clearly
    /// a new question about a council service rather than an appointment step response.
    /// Used to release the user from the flow without them having to explicitly cancel.
    /// </summary>
    private static bool IsUnrelatedToAppointmentFlow(string normMsg)
    {
        if (string.IsNullOrWhiteSpace(normMsg)) return false;

        // If the message starts with a question word and contains a service keyword,
        // it's almost certainly a new question, not a date/time/name input.
        var questionStarters = new[] { "how", "what", "where", "when", "why", "who", "can i", "do i", "is there", "am i" };
        bool startsWithQuestion = questionStarters.Any(q => normMsg.StartsWith(q, StringComparison.OrdinalIgnoreCase));

        // Service-domain keywords indicate a new topic
        bool hasServiceKeyword =
            normMsg.Contains("council tax") || normMsg.Contains("bin") || normMsg.Contains("waste") ||
            normMsg.Contains("benefit") || normMsg.Contains("housing") || normMsg.Contains("school") ||
            normMsg.Contains("planning") || normMsg.Contains("library") || normMsg.Contains("contact") ||
            normMsg.Contains("payment") || normMsg.Contains("apply") || normMsg.Contains("eligible");

        return startsWithQuestion && hasServiceKeyword;
    }

    private static bool IsAppointmentCancelIntent(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return false;
        return msg.Contains("cancel") ||
               msg.Contains("stop") ||
               msg.Contains("never mind") ||
               msg.Contains("nevermind") ||
               msg.Contains("start over") ||
               msg.Contains("forget it") ||
               msg.Contains("abort") ||
               msg.Contains("exit") ||
               msg.Contains("quit") ||
               msg.Contains("don't want") ||
               msg.Contains("no thanks") ||
               msg.Equals("no", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFormStartIntent(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return false;
        return msg.Contains("fill in a form") ||
               msg.Contains("fill in the form") ||
               msg.Contains("help with a form") ||
               msg.Contains("help me fill") ||
               msg.Contains("start an application") ||
               msg.Contains("apply for benefits") && msg.Contains("form") ||
               msg.Contains("benefits form") ||
               msg.Contains("housing form") ||
               msg.Contains("school form") ||
               msg.Contains("blue badge form") ||
               msg.Contains("council tax form") ||
               msg.Contains("guided application") ||
               msg.Contains("step by step") && (msg.Contains("form") || msg.Contains("apply"));
    }
    private static bool IsFormCancelIntent(string msg)
{
    if (string.IsNullOrWhiteSpace(msg)) return false;

    return msg.Contains("cancel form") ||
           msg.Equals("cancel", StringComparison.OrdinalIgnoreCase) ||
           msg.Contains("stop form") ||
           msg.Contains("quit form") ||
           msg.Contains("exit form") ||
           msg.Contains("close form") ||
           msg.Contains("nevermind") ||
           msg.Contains("never mind");
}

    private static string DetectFormType(string msg)
    {
        if (msg.Contains("benefit") || msg.Contains("housing benefit") || msg.Contains("council tax support"))
            return "benefits";
        if (msg.Contains("housing") && !msg.Contains("housing benefit"))
            return "housing";
        if (msg.Contains("school"))
            return "school";
        if (msg.Contains("blue badge"))
            return "blue_badge";
        if (msg.Contains("council tax change") || msg.Contains("change of address") && msg.Contains("council tax"))
            return "council_tax_change";
        return "";
    }

    private static bool IsHousingUrgentIntent(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return false;
        return msg.Contains("homeless") ||
               msg.Contains("eviction") ||
               msg.Contains("evicted") ||
               msg.Contains("domestic abuse") ||
               msg.Contains("domestic violence") ||
               msg.Contains("rough sleeping") ||
               msg.Contains("nowhere to sleep") ||
               msg.Contains("emergency housing") ||
               msg.Contains("temporary accommodation");
    }

    private static bool IsSchoolFinderIntent(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return false;
        return (msg.Contains("find") || msg.Contains("search") || msg.Contains("show") || msg.Contains("list")) &&
                (msg.Contains("school") || msg.Contains("primary") || msg.Contains("secondary")) ||
               msg.Contains("schools near") ||
               msg.Contains("admissions") ||
               msg.Contains("school deadline") ||
               msg.Contains("in-year") || msg.Contains("in year transfer") ||
               msg.Contains("starting school") ||
               msg.Contains("school place");
    }

    private static bool IsBinTypeGuideIntent(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return false;
        return (msg.Contains("what goes") || msg.Contains("what can") || msg.Contains("what do")) &&
               (msg.Contains("bin") || msg.Contains("recycling") || msg.Contains("waste")) ||
               msg.Contains("bin guide") ||
               msg.Contains("recycling guide") ||
               msg.Contains("what goes in the") ||
               msg.Contains("can i put") && msg.Contains("bin");
    }

    private static (string reply, List<string> suggestions) GetBinTypeGuide(string msg)
    {
        // Specific bin type guides
        if (msg.Contains("recycling") || msg.Contains("blue bin"))
        {
            return (
                "♻️ **Blue Recycling Bin — What Goes In:**\n\n" +
                "✅ Paper & cardboard\n" +
                "✅ Glass bottles and jars\n" +
                "✅ Plastic bottles and containers\n" +
                "✅ Food and drink tins and cans\n" +
                "✅ Aerosol cans (empty)\n\n" +
                "❌ **Do NOT put in:**\n" +
                "❌ Food waste\n" +
                "❌ Nappies or hygiene products\n" +
                "❌ Plastic bags (take to supermarket collection points)\n" +
                "❌ Pyrex, drinking glasses, or ceramics\n" +
                "❌ Polystyrene\n\n" +
                "🔗 Full guide: https://www.bradford.gov.uk/recycling-and-waste/wheeled-bins-and-recycling-containers/what-goes-in-your-bins/",
                new List<string> { "What goes in my general waste bin?", "What goes in my garden waste bin?", "Report a missed bin", "When is my bin collection?" }
            );
        }

        if (msg.Contains("garden") || msg.Contains("brown bin") || msg.Contains("green bin"))
        {
            return (
                "🌿 **Garden Waste Bin — What Goes In:**\n\n" +
                "✅ Grass clippings\n" +
                "✅ Leaves\n" +
                "✅ Twigs and small branches\n" +
                "✅ Hedge trimmings\n" +
                "✅ Flowers and plants\n\n" +
                "❌ **Do NOT put in:**\n" +
                "❌ Food waste\n" +
                "❌ Soil or turf\n" +
                "❌ Large branches (take to recycling centre)\n" +
                "❌ Treated or painted wood\n\n" +
                "⚠️ Garden waste collection is a **subscription service** in Bradford. You must register to use this bin.\n" +
                "🔗 https://www.bradford.gov.uk/recycling-and-waste/wheeled-bins-and-recycling-containers/",
                new List<string> { "What goes in my blue recycling bin?", "What goes in my general waste bin?", "Garden waste subscription", "Report a missed bin" }
            );
        }

        // General waste (black/grey bin)
        return (
            "🗑️ **General Waste Bin (Black/Grey) — What Goes In:**\n\n" +
            "This bin is for waste that cannot be recycled or composted, such as:\n\n" +
            "✅ Nappies and hygiene products\n" +
            "✅ Polystyrene\n" +
            "✅ Ceramics and Pyrex\n" +
            "✅ Plastic bags and wrapping\n" +
            "✅ Broken glass (wrapped carefully)\n\n" +
            "❌ **Do NOT put in:**\n" +
            "❌ Recycling (paper, glass, plastic, cans → blue bin)\n" +
            "❌ Garden waste → garden waste subscription bin\n" +
            "❌ Electrical items → take to recycling centre\n" +
            "❌ Medicines or sharps → take to a pharmacy\n\n" +
            "🔗 https://www.bradford.gov.uk/recycling-and-waste/wheeled-bins-and-recycling-containers/what-goes-in-your-bins/",
            new List<string> { "What goes in my blue recycling bin?", "What goes in my garden waste bin?", "Find recycling centre", "Report a missed bin" }
        );
    }

    private static string BuildFormSummaryReply(Models.FormDraftSummary summary)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"✅ **{summary.FormTitle} — Draft Summary**\n");
        sb.AppendLine(summary.Message);
        sb.AppendLine();

        foreach (var field in summary.Fields)
            sb.AppendLine($"• **{field.Key}:** {field.Value}");

        sb.AppendLine();
        sb.AppendLine($"🔗 To submit your application, visit:\n{summary.NextStepsUrl}");
        sb.AppendLine("\n*Nothing has been submitted yet. Please review the above carefully before proceeding.*");

        return sb.ToString();
    }

    private static string BuildSchoolResultsReply(List<Models.NearbyServiceResult> schools, string postcode)
    {
        if (!schools.Any())
            return $"No schools found near {postcode} in the Bradford district. Please try a different postcode or contact the School Admissions team on 01274 439200.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"🏫 **Schools near {postcode}:**\n");

        foreach (var s in schools.Take(4))
        {
            sb.AppendLine($"**{s.Name}** (~{s.EstimatedDistanceMiles} miles)");
            sb.AppendLine($"📍 {s.Address}");
            sb.AppendLine($"ℹ️ {s.Notes}");
            if (!string.IsNullOrWhiteSpace(s.Phone)) sb.AppendLine($"📞 {s.Phone}");
            sb.AppendLine();
        }

        sb.AppendLine("🔗 Admissions: https://www.bradford.gov.uk/education-and-skills/school-admissions/apply-for-a-place-at-one-of-bradford-districts-schools/");
        return sb.ToString();
    }

    // ── Also update BuildSuggestions to handle new services ──────────────────────
    // (override the default case and add new service cases via an extension below)
}
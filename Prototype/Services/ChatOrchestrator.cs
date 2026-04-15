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
            "mobility support", "parking badge"
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
            "contact us", "contact the council", "contact council", "contact",
            "telephone", "phone number", "customer service number", "customer service",
            "customer services", "call the council", "contact details",
            "email", "email the council", "council email",
            "opening hours", "opening times", "office hours",
            "complaint", "complaints", "make a complaint",
            "feedback", "give feedback",
            "social media", "facebook", "twitter", "x",
            "post", "postal address", "by post",
            "send documents", "send paperwork",
            "visit the council", "in person", "council office", "office",
            "departments", "contact department",
            "support", "contact support",
            "online chat", "web chat", "live chat",
            "emergency contact"
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

    private readonly HashSet<string> _genericMessages = new(StringComparer.OrdinalIgnoreCase)
    {
        "help", "hi", "hello", "hey", "ok", "okay", "thanks", "thank you", "please"
    };

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

            if (string.IsNullOrWhiteSpace(detectedService) &&
                IsShortFollowUp(normMsg) &&
                !string.IsNullOrWhiteSpace(lastService))
            {
                normMsg = $"{Normalize(lastService)} {normMsg}";
                detectedService = DetectService(normMsg, _strongServiceTriggers);
            }


        // 1. Generic greetings / vague starter prompts
        if (_genericMessages.Contains(normMsg))
        {
            var genericReply =
                "I can help with Council Tax, Waste and Bins, Benefits and Support, and School Admissions. What would you like help with?";
            var greetingSuggestions = new List<string>
            {
                "How do I check my Council Tax balance?",
                "When is my bin collection?",
                "How do I apply for a Blue Badge?",
                "How do I apply for free school meals?"
            };

            SaveConversation(sessionId, message, genericReply, "Unknown", "greeting", greetingSuggestions);
            return (genericReply, "Unknown", "", 0, greetingSuggestions);
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

                if (IsContactIntent(normMsg))
        {
            string reply;
            var suggestions = new List<string>();
            var nextUrl = "https://www.bradford.gov.uk/contact-us/";

            if (normMsg.Contains("phone number") || normMsg.Contains("telephone") || normMsg.Contains("customer service"))
            {
                reply = "You can contact Bradford Council through the council contact page, which includes the main phone contact details and service contact options.";
                suggestions.AddRange(new[]
                {
                    "Email the council",
                    "Opening hours",
                    "Visit the council in person",
                    "Make a complaint"
                });
            }
            else if (normMsg.Contains("email"))
            {
                reply = "You can use the council contact page to find the appropriate online or email contact option for your enquiry.";
                suggestions.AddRange(new[]
                {
                    "Council phone number",
                    "Send documents to the council",
                    "Make a complaint",
                    "Contact a department"
                });
            }
            else if (normMsg.Contains("opening hours") || normMsg.Contains("opening times") || normMsg.Contains("office hours"))
            {
                reply = "Council opening hours and service contact arrangements are listed on the Bradford Council contact page. Some services may have different hours, so it is best to check the relevant contact details there.";
                suggestions.AddRange(new[]
                {
                    "Council phone number",
                    "Visit the council in person",
                    "Where is the council office?",
                    "Contact a department"
                });
            }
            else if (normMsg.Contains("complaint") || normMsg.Contains("feedback"))
            {
                reply = "You can use Bradford Council’s contact and complaints information to make a complaint or provide feedback about a council service.";
                suggestions.AddRange(new[]
                {
                    "Make a complaint",
                    "Give feedback",
                    "Council phone number",
                    "Contact the council"
                });
            }
            else if (normMsg.Contains("social media"))
            {
                reply = "Bradford Council contact channels and updates are listed through the council website. Check the contact page and official site links for the latest communication options.";
                suggestions.AddRange(new[]
                {
                    "Sign up for email alerts",
                    "Contact the council",
                    "Council phone number",
                    "Opening hours"
                });
            }
            else if (normMsg.Contains("post") || normMsg.Contains("postal") || normMsg.Contains("send documents"))
            {
                reply = "Use the Bradford Council contact page to find the correct postal or document submission details for your enquiry, as these can vary by service.";
                suggestions.AddRange(new[]
                {
                    "Send documents to the council",
                    "Email the council",
                    "Contact a department",
                    "Council phone number"
                });
            }
            else if (normMsg.Contains("in person") || normMsg.Contains("visit the council") || normMsg.Contains("council office"))
            {
                reply = "You can check Bradford Council contact information for office locations, visiting details, and the best service point for your enquiry.";
                suggestions.AddRange(new[]
                {
                    "Where is the council office?",
                    "Opening hours",
                    "Council phone number",
                    "Find council office"
                });
            }
            else if (normMsg.Contains("online chat") || normMsg.Contains("live chat"))
            {
                reply = "Check the Bradford Council contact page to see which contact options are currently available for your enquiry, including any online support channels.";
                suggestions.AddRange(new[]
                {
                    "Contact the council",
                    "Council phone number",
                    "Email the council",
                    "Opening hours"
                });
            }
            else
            {
                reply = "You can contact Bradford Council through the council contact page for phone, online, post, complaints, and service-specific contact details.";
                suggestions.AddRange(new[]
                {
                    "Council phone number",
                    "Email the council",
                    "Opening hours",
                    "Make a complaint"
                });
            }

            SaveConversation(sessionId, message, reply, "Contact Us", "contact", suggestions);
            return (reply, "Contact Us", nextUrl, 1.0f, suggestions);
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
        var threshold = _config.GetValue("Retrieval:Threshold", 0.45f);

        // 9. Retrieve candidate chunks
        List<(FaqChunk chunk, float score)> top =
            !string.IsNullOrWhiteSpace(detectedService)
                ? _retrieval.TopKInService(qEmb, detectedService, 4)
                : _retrieval.TopK(qEmb, 4);

        var best = top.FirstOrDefault();
        var bestChunk = best.chunk;
        var bestScore = best.score;

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

        // 12. If retrieval is weak, still let the agent try first
        if (bestChunk == null || bestScore < threshold)
        {
            var weakContextAgentResult = await _langChain.RunAgentAsync(message, serviceHint, context, history);

            var weakToolHandled = HandleAgentToolResponse(
                sessionId,
                message,
                weakContextAgentResult,
                bestScore);

            if (weakToolHandled.hasToolResponse)
                return weakToolHandled.result;

            var weakReply = weakContextAgentResult.answer;
            var weakService = string.IsNullOrWhiteSpace(weakContextAgentResult.service)
                ? serviceHint
                : weakContextAgentResult.service;
            var weakNextStepsUrl = weakContextAgentResult.nextStepsUrl ?? "";
            var weakSuggestions = BuildSuggestions(weakService, detectedIntent);

            if (!string.IsNullOrWhiteSpace(weakReply) && !IsGenericOrWeakReply(weakReply))
            {
                SaveConversation(sessionId, message, weakReply, weakService, detectedIntent, weakSuggestions);
                return (weakReply, weakService, weakNextStepsUrl, bestScore, weakSuggestions);
            }

            // Prefer the most specific service signal available before asking for clarification.
            var clarificationService =
                !string.IsNullOrWhiteSpace(detectedService) && !string.Equals(detectedService, "Unknown", StringComparison.OrdinalIgnoreCase) ? detectedService :
                !string.IsNullOrWhiteSpace(serviceHint)     && !string.Equals(serviceHint,     "Unknown", StringComparison.OrdinalIgnoreCase) ? serviceHint :
                !string.IsNullOrWhiteSpace(lastService)     && !string.Equals(lastService,     "Unknown", StringComparison.OrdinalIgnoreCase) ? lastService :
                bestChunk?.Service ?? "Unknown";

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
    if (string.IsNullOrWhiteSpace(normMsg))
        return "";

    var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    foreach (var kv in triggers)
    {
        var service = kv.Key;
        var score = 0;

        foreach (var trigger in kv.Value)
        {
            var normalizedTrigger = Normalize(trigger);
            if (string.IsNullOrWhiteSpace(normalizedTrigger))
                continue;

            if (!normMsg.Contains(normalizedTrigger))
                continue;

            var wordCount = normalizedTrigger
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Length;

            // multi-word triggers are usually stronger signals
            score += wordCount >= 3 ? 4 :
                     wordCount == 2 ? 3 : 1;
        }

        if (score > 0)
            scores[service] = score;
    }

    if (scores.Count == 0)
        return "";

    return scores
        .OrderByDescending(x => x.Value)
        .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
        .First()
        .Key;
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
    /// Returns a short, service-specific clarification question.
    /// Service priority: serviceHint → lastService → generic fallback.
    /// </summary>
    private static string BuildClarificationReply(string serviceHint, string lastService, string normMsg)
    {
        var svc = !string.IsNullOrWhiteSpace(serviceHint) && !string.Equals(serviceHint, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? serviceHint
            : !string.IsNullOrWhiteSpace(lastService) && !string.Equals(lastService, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? lastService
            : "Unknown";

        return svc switch
        {
            "Council Tax"        => "Do you want help with your balance, payment, discount, or support?",
            "Waste & Bins"       => "Do you mean collection day, missed bin, or requesting a new bin?",
            "Benefits & Support" => "Do you want to check eligibility, apply, or see what evidence is needed?",
            "Education"          => "Do you mean school admissions, deadlines, in-year transfer, or nearby schools?",
            "Housing"            => "Do you need help with homelessness, housing support, repairs, or finding a home?",
            "Planning"           => "Do you mean checking an application, applying, or commenting on a proposal?",
            "Libraries"          => "Do you want help with renewals, joining, e-books, or fines?",
            _                    => "Could you tell me a bit more about what you need? For example, is this about Council Tax, housing, bins, benefits, or schools?"
        };
    }

    /// <summary>
    /// Returns targeted suggestion chips to accompany a clarification reply.
    /// Follows the same service-priority order as BuildClarificationReply.
    /// </summary>
    private static List<string> BuildClarificationSuggestions(string serviceHint, string lastService, string normMsg)
    {
        var svc = !string.IsNullOrWhiteSpace(serviceHint) && !string.Equals(serviceHint, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? serviceHint
            : !string.IsNullOrWhiteSpace(lastService) && !string.Equals(lastService, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? lastService
            : "Unknown";

        return svc switch
        {
            "Council Tax"        => new List<string> { "Balance", "Make a payment", "Apply for a discount", "Council Tax Support" },
            "Waste & Bins"       => new List<string> { "Collection day", "Missed bin", "Request a new bin" },
            "Benefits & Support" => new List<string> { "Check eligibility", "How to apply", "What evidence is needed" },
            "Education"          => new List<string> { "School admissions", "Admission deadlines", "In-year transfer", "Find schools near me" },
            "Housing"            => new List<string> { "Homelessness help", "Housing support", "Property repairs", "Find a home" },
            "Planning"           => new List<string> { "Check an application", "Apply for planning permission", "Comment on a proposal" },
            "Libraries"          => new List<string> { "Renew books", "Join the library", "Borrow e-books", "Library fines" },
            _                    => new List<string> { "Council Tax", "Waste & Bins", "Benefits & Support", "Housing" }
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
            "**General Waste Bin (Black/Grey) — What Goes In:**\n\n" +
            "This bin is for waste that cannot be recycled or composted, such as:\n\n" +
            "Nappies and hygiene products\n" +
            "Polystyrene\n" +
            "Ceramics and Pyrex\n" +
            "Plastic bags and wrapping\n" +
            "Broken glass (wrapped carefully)\n\n" +
            "**Do NOT put in:**\n" +
            "Recycling (paper, glass, plastic, cans → blue bin)\n" +
            "Garden waste → garden waste subscription bin\n" +
            "Electrical items → take to recycling centre\n" +
            "Medicines or sharps → take to a pharmacy\n\n" +
            "https://www.bradford.gov.uk/recycling-and-waste/wheeled-bins-and-recycling-containers/what-goes-in-your-bins/",
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
    private static bool IsContactIntent(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg))
            return false;

        return msg.Contains("contact") ||
            msg.Contains("phone number") ||
            msg.Contains("telephone") ||
            msg.Contains("customer service") ||
            msg.Contains("email") ||
            msg.Contains("opening hours") ||
            msg.Contains("opening times") ||
            msg.Contains("complaint") ||
            msg.Contains("feedback") ||
            msg.Contains("social media") ||
            msg.Contains("post") ||
            msg.Contains("postal") ||
            msg.Contains("send documents") ||
            msg.Contains("in person") ||
            msg.Contains("visit the council") ||
            msg.Contains("council office") ||
            msg.Contains("online chat") ||
            msg.Contains("live chat") ||
            msg.Contains("departments") ||
            msg.Contains("emergency contact");
    }
}
using System.Collections.Concurrent;
using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

/// <summary>
/// Manages guided, step-by-step form-filling flows in the chat interface.
///
/// Each form type is defined as an ordered list of FormField questions.
/// The service advances through them one turn at a time, collecting validated answers.
/// When all questions are answered it returns a FormDraftSummary for the user to review.
///
/// Forms are NOT auto-submitted — the user must confirm before any submission.
/// FUTURE INTEGRATION POINT: hook SubmitForm() to real council online forms.
/// </summary>
public class FormFlowService
{
    // ── Form definitions ─────────────────────────────────────────────────────────

    private static readonly Dictionary<string, (string Title, string NextStepsUrl, List<FormField> Fields)> Forms
        = new(StringComparer.OrdinalIgnoreCase)
    {
        ["benefits"] = (
            "Housing Benefit & Council Tax Support Application",
            "https://www.bradford.gov.uk/benefits/applying-for-benefits/housing-benefit-and-council-tax-reduction/",
            new List<FormField>
            {
                new() { Key = "full_name",        Label = "What is your full name?",                                          Type = "text" },
                new() { Key = "dob",              Label = "What is your date of birth? (e.g. 15/03/1985)",                   Type = "date" },
                new() { Key = "address",          Label = "What is your current address?",                                   Type = "text" },
                new() { Key = "postcode",         Label = "What is your postcode?",                                          Type = "text" },
                new() { Key = "national_ins",     Label = "What is your National Insurance number?",                         Type = "text",
                    Hint = "Format: 2 letters, 6 digits, 1 letter — e.g. AB123456C" },
                new() { Key = "employment",       Label = "What is your current employment status?",                         Type = "select",
                    Options = new() { "Employed", "Self-employed", "Unemployed", "Retired", "Unable to work due to illness" } },
                new() { Key = "weekly_income",    Label = "What is your total weekly household income (including benefits)?", Type = "text",
                    Hint = "Include wages, Universal Credit, pensions, and any other income." },
                new() { Key = "savings",          Label = "Do you have savings or investments over £6,000?",                 Type = "yesno",
                    Options = new() { "Yes", "No" } },
                new() { Key = "num_dependants",   Label = "How many dependants (children or adults) live with you?",         Type = "text" },
                new() { Key = "disability",       Label = "Does anyone in your household have a disability or health condition?", Type = "yesno",
                    Options = new() { "Yes", "No" } },
            }
        ),

        ["housing"] = (
            "Housing Application",
            "https://www.bradford.gov.uk/housing/finding-a-home/how-can-i-find-a-home/",
            new List<FormField>
            {
                new() { Key = "full_name",        Label = "What is your full name?",                                       Type = "text" },
                new() { Key = "dob",              Label = "What is your date of birth? (e.g. 15/03/1985)",                Type = "date" },
                new() { Key = "current_address",  Label = "What is your current address?",                                Type = "text" },
                new() { Key = "postcode",         Label = "What is your postcode?",                                       Type = "text" },
                new() { Key = "current_tenure",   Label = "What is your current housing situation?",                     Type = "select",
                    Options = new() { "Private rented", "Council/social housing", "Staying with family/friends", "Rough sleeping", "Hostel/temporary accommodation", "Own my home" } },
                new() { Key = "household_size",   Label = "How many people, including yourself, will be in your household?", Type = "text" },
                new() { Key = "bedrooms_needed",  Label = "How many bedrooms do you need?",                               Type = "select",
                    Options = new() { "1", "2", "3", "4", "5+" } },
                new() { Key = "medical_need",     Label = "Does anyone in your household have a medical need that affects the type of housing required?", Type = "yesno",
                    Options = new() { "Yes", "No" } },
                new() { Key = "reason",           Label = "Briefly, why are you applying for council housing?",            Type = "text" },
            }
        ),

        ["school"] = (
            "School Place Application",
            "https://www.bradford.gov.uk/education-and-skills/school-admissions/apply-for-a-place-at-one-of-bradford-districts-schools/",
            new List<FormField>
            {
                new() { Key = "parent_name",      Label = "What is your full name (parent or guardian)?",                  Type = "text" },
                new() { Key = "child_name",       Label = "What is your child's full name?",                               Type = "text" },
                new() { Key = "child_dob",        Label = "What is your child's date of birth? (e.g. 01/09/2019)",         Type = "date" },
                new() { Key = "address",          Label = "What is your home address?",                                    Type = "text" },
                new() { Key = "postcode",         Label = "What is your postcode?",                                        Type = "text" },
                new() { Key = "school_year",      Label = "Which year group is your child applying for?",                  Type = "select",
                    Options = new() { "Reception (Year R)", "Year 1", "Year 2", "Year 3", "Year 4", "Year 5", "Year 6", "Year 7 (Secondary)", "Year 8", "Year 9", "Year 10", "Year 11" } },
                new() { Key = "school_preference_1", Label = "What is your first choice of school?",                       Type = "text",
                    Hint = "Enter the name of your preferred school." },
                new() { Key = "school_preference_2", Label = "What is your second choice of school? (optional — type 'none' to skip)", Type = "text" },
                new() { Key = "siblings",         Label = "Does your child have a sibling already attending the preferred school?", Type = "yesno",
                    Options = new() { "Yes", "No" } },
                new() { Key = "ehcp",             Label = "Does your child have an Education, Health and Care Plan (EHCP)?", Type = "yesno",
                    Options = new() { "Yes", "No" } },
            }
        ),

        ["council_tax_change"] = (
            "Change of Address or Circumstances — Council Tax",
            "https://www.bradford.gov.uk/council-tax/report-a-change-of-address-or-circumstances/report-a-change-or-ask-a-question-about-your-council-tax/",
            new List<FormField>
            {
                new() { Key = "full_name",        Label = "What is your full name?",                                       Type = "text" },
                new() { Key = "account_ref",      Label = "What is your Council Tax account reference number?",            Type = "text",
                    Hint = "Found on your Council Tax bill, starts with 8 digits." },
                new() { Key = "change_type",      Label = "What is the nature of your change?",                           Type = "select",
                    Options = new() { "Moved to a new address", "Someone has moved in or out", "Change in discount or exemption", "Changed my name", "Other" } },
                new() { Key = "old_address",      Label = "What was your previous address?",                              Type = "text" },
                new() { Key = "new_address",      Label = "What is your new address?",                                    Type = "text" },
                new() { Key = "change_date",      Label = "What was the date of the change? (e.g. 01/04/2026)",           Type = "date" },
            }
        ),

        ["blue_badge"] = (
            "Blue Badge Application",
            "https://www.bradford.gov.uk/transport-and-travel/transport-for-disabled-people/blue-badge-scheme/",
            new List<FormField>
            {
                new() { Key = "full_name",        Label = "What is the full name of the applicant?",                       Type = "text" },
                new() { Key = "dob",              Label = "What is the applicant's date of birth? (e.g. 15/03/1970)",      Type = "date" },
                new() { Key = "address",          Label = "What is the applicant's home address?",                         Type = "text" },
                new() { Key = "postcode",         Label = "What is the postcode?",                                         Type = "text" },
                new() { Key = "disability_type",  Label = "Which best describes the applicant's disability or condition?", Type = "select",
                    Options = new() { "Walks less than 80 metres without severe discomfort", "Cannot walk at all", "Severe upper limb disability (driver only)", "Cognitive impairment (e.g. severe dementia)", "Other significant disability" } },
                new() { Key = "dla_pip",          Label = "Does the applicant receive PIP, DLA, or another disability benefit?", Type = "select",
                    Options = new() { "Yes — PIP (8 points or more for 'moving around')", "Yes — DLA higher mobility component", "Yes — other benefit", "No" } },
                new() { Key = "existing_badge",   Label = "Does the applicant already have a Blue Badge?",                 Type = "yesno",
                    Options = new() { "Yes — renewing", "No — new application" } },
            }
        ),
    };

    // ── In-memory form sessions ──────────────────────────────────────────────────
    private readonly ConcurrentDictionary<string, FormSession> _sessions = new();
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(30);

    // ─────────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Returns the list of available form type keys for display to the user.</summary>
    public IReadOnlyList<string> GetAvailableFormTypes()
        => Forms.Keys.ToList();

    /// <summary>Returns a user-friendly title for a form type key.</summary>
    public string GetFormTitle(string formType)
        => Forms.TryGetValue(formType, out var def) ? def.Title : formType;

    /// <summary>Starts a new form flow for a session. Returns the first question.</summary>
    public FormStepResponse StartForm(string sessionId, string formType)
    {
        CleanupExpiredSessions();

        if (!Forms.TryGetValue(formType, out var def))
        {
            return new FormStepResponse
            {
                IsComplete   = false,
                NextQuestion = "I don't recognise that form type. Please choose from: benefits, housing, school, council_tax_change, or blue_badge.",
                StepNumber   = 0,
                TotalSteps   = 0
            };
        }

        var session = new FormSession
        {
            SessionId   = sessionId,
            FormType    = formType,
            CurrentStep = 0,
            StartedAt   = DateTime.UtcNow
        };

        _sessions[sessionId] = session;

        return BuildStepResponse(session, def.Fields);
    }

    /// <summary>
    /// Accepts the user's answer for the current step and advances the form.
    /// Returns the next question, or the completed summary.
    /// </summary>
    public FormStepResponse SubmitAnswer(string sessionId, string answer)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return new FormStepResponse
            {
                IsComplete   = false,
                NextQuestion = "No active form found. Please start a new form by saying 'start benefits form' or similar.",
                StepNumber   = 0,
                TotalSteps   = 0
            };
        }

        if (!Forms.TryGetValue(session.FormType, out var def))
        {
            return new FormStepResponse { IsComplete = false, NextQuestion = "Form type not found.", StepNumber = 0, TotalSteps = 0 };
        }

        var fields = def.Fields;

        if (session.CurrentStep >= fields.Count)
        {
            return BuildCompletedResponse(session, def);
        }

        var currentField = fields[session.CurrentStep];

        // Validate required fields
        if (currentField.Required && string.IsNullOrWhiteSpace(answer))
        {
            return new FormStepResponse
            {
                IsComplete   = false,
                NextQuestion = $"This field is required. {currentField.Label}",
                FieldKey     = currentField.Key,
                FieldType    = currentField.Type,
                Options      = currentField.Options,
                Hint         = currentField.Hint,
                StepNumber   = session.CurrentStep + 1,
                TotalSteps   = fields.Count
            };
        }

        // Save the answer
        session.CollectedData[currentField.Key] = answer.Trim();
        session.CurrentStep++;

        // Check if completed
        if (session.CurrentStep >= fields.Count)
        {
            session.IsComplete = true;
            return BuildCompletedResponse(session, def);
        }

        return BuildStepResponse(session, fields);
    }

    /// <summary>Returns whether a session has an active form in progress.</summary>
    public bool HasActiveForm(string sessionId)
        => _sessions.TryGetValue(sessionId, out var s) && !s.IsComplete;

    /// <summary>Returns the current form session, if any.</summary>
    public FormSession? GetSession(string sessionId)
        => _sessions.TryGetValue(sessionId, out var s) ? s : null;

    /// <summary>Clears the form session (e.g. user cancels).</summary>
    public void ClearSession(string sessionId)
        => _sessions.TryRemove(sessionId, out _);

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static FormStepResponse BuildStepResponse(FormSession session, List<FormField> fields)
    {
        var field = fields[session.CurrentStep];
        return new FormStepResponse
        {
            IsComplete   = false,
            NextQuestion = field.Label,
            FieldKey     = field.Key,
            FieldType    = field.Type,
            Options      = field.Options,
            Hint         = field.Hint,
            StepNumber   = session.CurrentStep + 1,
            TotalSteps   = fields.Count
        };
    }

    private static FormStepResponse BuildCompletedResponse(
        FormSession session,
        (string Title, string NextStepsUrl, List<FormField> Fields) def)
    {
        // Build a human-readable label map
        var labeledData = new Dictionary<string, string>();
        foreach (var field in def.Fields)
        {
            if (session.CollectedData.TryGetValue(field.Key, out var val))
                labeledData[field.Label.TrimEnd('?').Trim()] = val;
        }

        return new FormStepResponse
        {
            IsComplete = true,
            StepNumber = def.Fields.Count,
            TotalSteps = def.Fields.Count,
            Summary = new FormDraftSummary
            {
                FormType     = session.FormType,
                FormTitle    = def.Title,
                Fields       = labeledData,
                Message      = "Here is a summary of the information you've provided. Please review it carefully before submitting. " +
                               "This is a draft — nothing has been submitted yet.",
                NextStepsUrl = def.NextStepsUrl
            }
        };
    }

    private void CleanupExpiredSessions()
    {
        var cutoff = DateTime.UtcNow - SessionTtl;
        foreach (var kvp in _sessions)
        {
            if (kvp.Value.StartedAt < cutoff)
                _sessions.TryRemove(kvp.Key, out _);
        }
    }
}

namespace CouncilChatbotPrototype.Models;

/// <summary>
/// The type of guided form the user is completing.
/// </summary>
public enum FormFlowType
{
    BenefitsApplication,
    HousingApplication,
    SchoolApplication,
    CouncilTaxChange,
    BlueBadgeApplication
}

/// <summary>
/// A single question/field in a guided form flow.
/// </summary>
public class FormField
{
    public string       Key      { get; set; } = "";       // e.g. "full_name"
    public string       Label    { get; set; } = "";       // e.g. "What is your full name?"
    public string       Type     { get; set; } = "text";   // text | date | select | yesno
    public bool         Required { get; set; } = true;
    public List<string> Options  { get; set; } = new();    // populated for select/yesno
    public string       Hint     { get; set; } = "";       // optional helper text shown below the question
}

/// <summary>
/// Tracks the state of an in-progress form flow for a session.
/// Stored in FormFlowService's ConcurrentDictionary keyed by sessionId.
/// </summary>
public class FormSession
{
    public string                     SessionId     { get; set; } = "";
    public string                     FormType      { get; set; } = "";
    public int                        CurrentStep   { get; set; } = 0;
    public Dictionary<string, string> CollectedData { get; set; } = new();
    public bool                       IsComplete    { get; set; } = false;
    public DateTime                   StartedAt     { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The response from /api/form/step — contains the next question or final summary.
/// </summary>
public class FormStepResponse
{
    public bool         IsComplete   { get; set; }
    public string       NextQuestion { get; set; } = "";
    public string       FieldKey     { get; set; } = "";
    public string       FieldType    { get; set; } = "text";
    public List<string> Options      { get; set; } = new();
    public string       Hint         { get; set; } = "";
    public int          StepNumber   { get; set; }
    public int          TotalSteps   { get; set; }

    /// <summary>Populated only when IsComplete = true.</summary>
    public FormDraftSummary? Summary { get; set; }
}

/// <summary>
/// The final draft summary shown to the user at the end of a form flow.
/// NOT auto-submitted — user must confirm before any submission.
/// </summary>
public class FormDraftSummary
{
    public string                     FormType     { get; set; } = "";
    public string                     FormTitle    { get; set; } = "";
    public Dictionary<string, string> Fields       { get; set; } = new();
    public string                     Message      { get; set; } = "";
    public string                     NextStepsUrl { get; set; } = "";
}

/// <summary>
/// Request body for /api/form/step.
/// </summary>
public class FormStepRequest
{
    public string SessionId { get; set; } = "";
    public string Answer    { get; set; } = "";
}

/// <summary>
/// Request body for /api/form/start.
/// </summary>
public class FormStartRequest
{
    public string SessionId { get; set; } = "";
    public string FormType  { get; set; } = "";   // "benefits" | "housing" | "school" | "council_tax_change" | "blue_badge"
}

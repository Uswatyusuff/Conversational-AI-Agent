using Microsoft.AspNetCore.Mvc;
using CouncilChatbotPrototype.Services;

namespace CouncilChatbotPrototype.Controllers;

/// <summary>
/// Dedicated school-finder endpoints consumed by the School Finder frontend panel.
///
/// GET /api/school/find?postcode=BD3+8PX&amp;type=all
///   Returns schools near a postcode.
///   type = "all" | "primary" | "secondary"  (default "all")
///   count = max results (default 5, max 10)
///
/// GET /api/school/admissions
///   Returns structured admissions links and deadline info.
/// </summary>
[ApiController]
public class SchoolController : ControllerBase
{
    private readonly SchoolFinderService _schools;

    public SchoolController(SchoolFinderService schools)
    {
        _schools = schools;
    }

    [HttpGet("/api/school/find")]
    public IActionResult Find(
        [FromQuery] string postcode,
        [FromQuery] string type  = "all",
        [FromQuery] int    count = 5)
    {
        if (string.IsNullOrWhiteSpace(postcode))
            return BadRequest(new { error = "Please provide a postcode." });

        count = Math.Clamp(count, 1, 10);

        var results = _schools.FindNearby(postcode.Trim(), type.Trim().ToLowerInvariant(), count);

        return Ok(new
        {
            postcode    = postcode.Trim().ToUpperInvariant(),
            schoolType  = type.Trim().ToLowerInvariant(),
            count       = results.Count,
            schools     = results.Select(s => new
            {
                name                  = s.Name,
                address               = s.Address,
                phone                 = s.Phone,
                notes                 = s.Notes,
                website               = s.Website,
                mapUrl                = s.MapUrl,
                estimatedDistanceMiles = s.EstimatedDistanceMiles,
            }),
            note = "Distances are estimates based on Bradford district postcode areas."
        });
    }

    [HttpGet("/api/school/admissions")]
    public IActionResult Admissions()
    {
        return Ok(new
        {
            links = new[]
            {
                new
                {
                    label       = "Apply for a school place",
                    description = "Online application for Reception, Year 7, and in-year transfers",
                    url         = "https://www.bradford.gov.uk/education-and-skills/school-admissions/apply-for-a-place-at-one-of-bradford-districts-schools/",
                    icon        = "📝"
                },
                new
                {
                    label       = "School admissions guidance",
                    description = "Criteria, catchment areas, and appeal process",
                    url         = "https://www.bradford.gov.uk/education-and-skills/school-admissions/",
                    icon        = "📚"
                },
                new
                {
                    label       = "In-year transfer",
                    description = "Apply for a place outside the normal admissions round",
                    url         = "https://www.bradford.gov.uk/education-and-skills/school-admissions/apply-for-a-place-at-one-of-bradford-districts-schools/",
                    icon        = "🔄"
                },
                new
                {
                    label       = "Free school meals",
                    description = "Check eligibility and apply online",
                    url         = "https://www.bradford.gov.uk/education-and-skills/free-school-meals/",
                    icon        = "🍽️"
                },
            },
            deadlines = new[]
            {
                new
                {
                    phase       = "Primary (Reception)",
                    opens       = "1 November 2025",
                    closes      = "15 January 2026",
                    offersDate  = "16 April 2026"
                },
                new
                {
                    phase       = "Secondary (Year 7)",
                    opens       = "1 September 2025",
                    closes      = "31 October 2025",
                    offersDate  = "1 March 2026"
                },
            },
            contact = new
            {
                team   = "Bradford School Admissions",
                phone  = "01274 439200",
                url    = "https://www.bradford.gov.uk/education-and-skills/school-admissions/"
            }
        });
    }
}

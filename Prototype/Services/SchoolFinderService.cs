using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

/// <summary>
/// Provides school information and admissions guidance for Bradford district.
///
/// MOCK DATA: School details are representative of actual Bradford schools.
/// READY FOR REAL API: Replace with Ofsted API, Get Information About Schools (GIAS),
/// or Bradford Council's own school finder at:
/// https://www.bradford.gov.uk/education-and-skills/school-admissions/
/// </summary>
public class SchoolFinderService
{
    private record SchoolRecord(
        string Name,
        string Type,          // "Primary" | "Secondary" | "Academy" | "Faith"
        string Area,          // Bradford, Keighley, Shipley, etc.
        string Address,
        string Postcode,
        string Phone,
        string Website,
        double Lat,
        double Lng,
        string AgeRange,      // e.g. "4–11"
        string OfstedRating,  // "Outstanding" | "Good" | "Requires Improvement"
        string Notes
    );

    // MOCK — representative Bradford schools
    private static readonly List<SchoolRecord> Schools = new()
    {
        // ── Primary Schools ───────────────────────────────────────────────────────
        new("Laisterdyke Leadership Academy", "Academy (Primary)", "Bradford",
            "Barkerend Road, Bradford, BD3 8QX", "BD3 8QX", "01274 662 150",
            "https://www.bradford.gov.uk/education-and-skills/school-admissions/",
            53.7980, -1.7220, "3–11", "Good", ""),

        new("Girlington Primary School", "Primary", "Bradford",
            "Legrams Lane, Bradford, BD7 2HE", "BD7 2HE", "01274 576 222",
            "https://www.bradford.gov.uk/education-and-skills/school-admissions/",
            53.7880, -1.7700, "4–11", "Good", ""),

        new("Fagley Primary School", "Primary", "Bradford",
            "Fagley Lane, Bradford, BD2 3LB", "BD2 3LB", "01274 631 543",
            "https://www.bradford.gov.uk/education-and-skills/school-admissions/",
            53.8120, -1.7300, "4–11", "Good", ""),

        new("Buttershaw St Paul's CE Primary", "Faith (C of E, Primary)", "Bradford",
            "Reevy Road West, Bradford, BD6 3NB", "BD6 3NB", "01274 679 420",
            "https://www.bradford.gov.uk/education-and-skills/school-admissions/",
            53.7700, -1.7850, "3–11", "Good", "Church of England school with faith admissions criteria."),

        new("Feversham Primary Academy", "Academy (Primary)", "Bradford",
            "Feversham Street, Bradford, BD3 0LB", "BD3 0LB", "01274 668 500",
            "https://www.bradford.gov.uk/education-and-skills/school-admissions/",
            53.7960, -1.7430, "4–11", "Outstanding", ""),

        // ── Secondary Schools ─────────────────────────────────────────────────────
        new("Bradford Grammar School", "Independent Grammar", "Bradford",
            "Keighley Road, Bradford, BD9 4JP", "BD9 4JP", "01274 542 492",
            "https://www.bradfordgrammar.com/",
            53.8110, -1.7780, "6–18", "N/A (Independent)", "Selective grammar school. Entrance exam required."),

        new("Hanson Academy", "Academy (Secondary)", "Bradford",
            "Sutton Avenue, Bradford, BD7 2EW", "BD7 2EW", "01274 551 374",
            "https://www.hansonacademy.co.uk/",
            53.7830, -1.7810, "11–18", "Good", "Co-educational academy with sixth form."),

        new("Beckfoot School", "Academy (Secondary)", "Bingley",
            "Wagon Lane, Bingley, BD16 1EE", "BD16 1EE", "01274 771 211",
            "https://www.beckfoot.org/",
            53.8448, -1.8285, "11–16", "Good", ""),

        new("Titus Salt School", "Academy (Secondary)", "Shipley",
            "Higher Coach Road, Baildon, Shipley, BD17 5RH", "BD17 5RH", "01274 584 266",
            "https://www.titussalt.co.uk/",
            53.8490, -1.7720, "11–18", "Outstanding", "Consistently high-performing school with sixth form."),

        new("Ilkley Grammar School", "Academy (Secondary)", "Ilkley",
            "Cowpasture Road, Ilkley, LS29 8TR", "LS29 8TR", "01943 608 424",
            "https://www.ilkleygrammar.org.uk/",
            53.9282, -1.8254, "11–18", "Good", "Partially selective — some places available via entrance assessment."),

        new("Belle Vue Girls' Academy", "Academy (Secondary, Girls)", "Bradford",
            "Thorn Lane, Bradford, BD9 5DT", "BD9 5DT", "01274 545 494",
            "https://www.bellevue.bdmat.org.uk/",
            53.8070, -1.7680, "11–16", "Good", "All-girls secondary academy."),

        new("Tong Leadership Academy", "Academy (Secondary)", "Bradford",
            "Westgate Hill Street, Bradford, BD4 6NR", "BD4 6NR", "01274 688 254",
            "https://www.bradford.gov.uk/education-and-skills/school-admissions/",
            53.7720, -1.7060, "11–16", "Good", ""),

        new("Keighley Girls Technology College", "Academy (Secondary, Girls)", "Keighley",
            "Greenhead Road, Keighley, BD20 6EB", "BD20 6EB", "01535 210 333",
            "https://www.keighleygirlstc.co.uk/",
            53.8750, -1.9090, "11–18", "Good", "All-girls academy with sixth form."),
    };

    // ─────────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns schools near a postcode, optionally filtered by type (primary/secondary).
    /// </summary>
    public List<NearbyServiceResult> FindNearby(string postcode, string schoolType = "all", int count = 5)
    {
        var query = schoolType.ToLowerInvariant();

        var filtered = Schools.Where(s =>
            query is "all" ||
            (query is "primary"   && s.Type.Contains("Primary",   StringComparison.OrdinalIgnoreCase)) ||
            (query is "secondary" && s.Type.Contains("Secondary", StringComparison.OrdinalIgnoreCase))
        ).ToList();

        return filtered
            .Select(s => new NearbyServiceResult
            {
                Type                  = "school",
                Name                  = s.Name,
                Address               = s.Address,
                Phone                 = s.Phone,
                OpeningHours          = $"Age range: {s.AgeRange} | Ofsted: {s.OfstedRating}",
                Website               = s.Website,
                Notes                 = BuildSchoolNotes(s),
                EstimatedDistanceMiles = EstimateDistance(postcode, s.Lat, s.Lng),
                MapUrl                = $"https://maps.google.com/?q={Uri.EscapeDataString(s.Address)}"
            })
            .OrderBy(r => r.EstimatedDistanceMiles)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Returns admissions guidance for a given school type and year group.
    /// </summary>
    public (string reply, List<string> suggestions) GetAdmissionsGuidance(string normMsg)
    {
        // Reception / primary intake
        if (normMsg.Contains("reception") || normMsg.Contains("starting school") ||
            normMsg.Contains("primary school") && (normMsg.Contains("apply") || normMsg.Contains("admission")))
        {
            return (
                "**Primary School Admissions in Bradford**\n\n" +
                "For children starting Reception (age 4–5) in September 2026:\n\n" +
                "📅 **Application window:** 1 November 2025 – 15 January 2026\n" +
                "📢 **Offers made:** 16 April 2026\n\n" +
                "**How to apply:**\n" +
                "1️⃣ Apply online at: https://www.bradford.gov.uk/education-and-skills/school-admissions/apply-for-a-place-at-one-of-bradford-districts-schools/\n" +
                "2️⃣ You can list up to 3 school preferences\n" +
                "3️⃣ Read each school's admissions criteria carefully (distance, siblings, faith)\n\n" +
                "**Important:** Living close to a school does not guarantee a place. Check each school's oversubscription criteria.",
                new List<string> { "Find primary schools near me", "What are the admissions criteria?", "Start school application form", "In-year transfer" }
            );
        }

        // Secondary school intake
        if (normMsg.Contains("secondary school") ||
            normMsg.Contains("year 7") ||
            normMsg.Contains("high school") && normMsg.Contains("apply"))
        {
            return (
                "**Secondary School Admissions in Bradford**\n\n" +
                "For children starting Year 7 (age 11–12) in September 2026:\n\n" +
                "📅 **Application window:** 1 September 2025 – 31 October 2025\n" +
                "📢 **Offers made:** 1 March 2026\n\n" +
                "**How to apply:**\n" +
                "1️⃣ Apply online at: https://www.bradford.gov.uk/education-and-skills/school-admissions/apply-for-a-place-at-one-of-bradford-districts-schools/\n" +
                "2️⃣ You can list up to 3 preferences\n" +
                "3️⃣ Check if any preferred school is selective (grammar or faith)\n\n" +
                "**Note for Bradford Grammar School:** This is an independent school — contact them directly about their entrance process.",
                new List<string> { "Find secondary schools near me", "What are the admissions criteria?", "Start school application form", "What is in-year transfer?" }
            );
        }

        // In-year transfer
        if (normMsg.Contains("in-year") || normMsg.Contains("in year") || normMsg.Contains("transfer") && normMsg.Contains("school"))
        {
            return (
                "**In-Year School Transfer**\n\n" +
                "An in-year transfer means applying for a school place outside the normal admissions round, for example if your child has moved to Bradford or needs to change school.\n\n" +
                "**How to apply:**\n" +
                "1️⃣ Complete an in-year application form at: https://www.bradford.gov.uk/education-and-skills/school-admissions/apply-for-a-place-at-one-of-bradford-districts-schools/\n" +
                "2️⃣ Contact the school directly to check availability\n" +
                "3️⃣ The council will coordinate the transfer if no places are immediately available\n\n" +
                "📞 Bradford School Admissions: **01274 439200**",
                new List<string> { "Find a school near me", "School admissions deadlines", "Free school meals", "School application form" }
            );
        }

        // Default
        return (
            "Bradford Council manages school admissions for all maintained schools in the district.\n\n" +
            "I can help you with:\n" +
            "• **Finding schools** near your postcode\n" +
            "• **Admissions guidance** for primary and secondary\n" +
            "• **In-year transfers**\n" +
            "• **Application deadlines**\n\n" +
            "What would you like help with?",
            new List<string> { "Find primary schools", "Find secondary schools", "When are the deadlines?", "In-year transfer" }
        );
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static string BuildSchoolNotes(SchoolRecord s)
    {
        var parts = new List<string> { $"Type: {s.Type}", $"Ages: {s.AgeRange}", $"Ofsted: {s.OfstedRating}" };
        if (!string.IsNullOrWhiteSpace(s.Notes)) parts.Add(s.Notes);
        return string.Join(" | ", parts);
    }

    private static double EstimateDistance(string postcode, double targetLat, double targetLng)
    {
        // Same approach as LocationService — use Bradford district centroids
        var centroids = new Dictionary<string, (double lat, double lng)>(StringComparer.OrdinalIgnoreCase)
        {
            ["BD1"]  = (53.7950, -1.7520), ["BD2"]  = (53.8080, -1.7300), ["BD3"]  = (53.7920, -1.7280),
            ["BD4"]  = (53.7780, -1.7270), ["BD5"]  = (53.7840, -1.7630), ["BD6"]  = (53.7680, -1.7780),
            ["BD7"]  = (53.7870, -1.7710), ["BD8"]  = (53.8020, -1.7680), ["BD9"]  = (53.8110, -1.7660),
            ["BD10"] = (53.8270, -1.7200), ["BD16"] = (53.8470, -1.8360), ["BD17"] = (53.8280, -1.7840),
            ["BD18"] = (53.8320, -1.7760), ["BD20"] = (53.9060, -1.9000), ["BD21"] = (53.8670, -1.9070),
            ["LS29"] = (53.9250, -1.8230),
        };

        var prefix = postcode?.Split(' ')[0].ToUpperInvariant() ?? "";
        var origin = centroids.TryGetValue(prefix, out var c)
    ? c
    : (lat: 53.7950, lng: -1.7520);

return Haversine(origin.lat, origin.lng, targetLat, targetLng);
    }

    private static double Haversine(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 3958.8;
        var dLat = ToRad(lat2 - lat1);
        var dLng = ToRad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
              * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return Math.Round(3958.8 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)), 1);
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}

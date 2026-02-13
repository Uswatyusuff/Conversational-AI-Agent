using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

public class FaqRepository
{
    private readonly List<FaqItem> _faqs;

    // ✅ Accept FAQs that were loaded ONCE at startup in Program.cs
    public FaqRepository(List<FaqItem> faqs)
    {
        _faqs = faqs ?? new List<FaqItem>();
    }

    public List<FaqItem> GetAll() => _faqs;
}
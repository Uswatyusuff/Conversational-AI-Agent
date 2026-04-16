using CouncilChatbotPrototype.Models;
public class LlmService
{
    public string GenerateResponse(string message, FaqItem? faq)
    {
        if (faq == null)
            return "I can help with Council Tax, Waste/Bins, Benefits, and School Admissions. Which service do you need?";

        return faq.Answer;
    }
}
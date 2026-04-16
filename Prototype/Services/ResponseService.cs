using CouncilChatbotPrototype.Models;

namespace CouncilChatbotPrototype.Services;

public class ResponseService
{
    public string GenerateReply(string message, FaqItem? faq, string finalService)
    {
        if (faq == null)
        {
            return "I can help with Council Tax, Waste/Bins, Benefits, and School Admissions. Which service do you need?";
        }

        // If Responses[] exists pick random; else Answer
        if (faq.Responses != null && faq.Responses.Count > 0)
            return faq.Responses[Random.Shared.Next(faq.Responses.Count)];

        return faq.Answer;
    }
}
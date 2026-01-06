using PdfTranslator.Models;

namespace PdfTranslator.Services
{
    public interface ITranslationProvider
    {
        Task<TranslationResult> TranslateAsync(string text, string sourceLanguage = "ru", string targetLanguage = "en");
        Task<List<TranslationResult>> TranslateBatchAsync(List<string> texts, string sourceLanguage = "ru", string targetLanguage = "en");
        bool IsConfigured();
    }
}

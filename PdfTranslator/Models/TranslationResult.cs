namespace PdfTranslator.Models
{
    public class TranslationResult
    {
        public string OriginalText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
        public double ConfidenceScore { get; set; }
        public int CharacterCount { get; set; }
    }
}

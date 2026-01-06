namespace PdfTranslator.Models
{
    public enum TranslationProvider
    {
        DeepL,
        MicrosoftTranslator,
        GoogleCloud
    }

    public enum OcrEngine
    {
        Tesseract,
        AzureDocumentIntelligence
    }

    /// <summary>
    /// DeepL translation mode - Document API preserves formatting better,
    /// Text API allows more control over extraction.
    /// </summary>
    public enum DeepLTranslationMode
    {
        /// <summary>
        /// Uses DeepL Document API - uploads entire PDF, best formatting preservation.
        /// </summary>
        DocumentTranslation,
        
        /// <summary>
        /// Uses DeepL Text API - extracts text, translates, overlays on PDF.
        /// </summary>
        TextTranslation
    }
}

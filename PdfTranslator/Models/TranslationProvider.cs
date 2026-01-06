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

    /// <summary>
    /// Microsoft Translator mode - Document API uses Azure Blob Storage for better formatting,
    /// Text API extracts text and overlays translation.
    /// </summary>
    public enum MicrosoftTranslationMode
    {
        /// <summary>
        /// Uses Azure Document Translation API - uploads to Blob Storage, best formatting preservation.
        /// Requires Azure Blob Storage connection and Document Translation endpoint.
        /// Pricing: ~$15 per million characters for translation + storage costs (~$0.02/GB/month).
        /// </summary>
        DocumentTranslation,
        
        /// <summary>
        /// Uses Microsoft Translator Text API - extracts text, translates, overlays on PDF.
        /// May have positioning issues with rotated pages.
        /// Pricing: ~$10 per million characters.
        /// </summary>
        TextTranslation
    }
}

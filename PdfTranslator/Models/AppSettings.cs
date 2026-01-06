namespace PdfTranslator.Models
{
    public class AppSettings
    {
        public TranslationProvider SelectedTranslationProvider { get; set; } = TranslationProvider.MicrosoftTranslator;
        public OcrEngine SelectedOcrEngine { get; set; } = OcrEngine.Tesseract;
        public DeepLTranslationMode DeepLMode { get; set; } = DeepLTranslationMode.DocumentTranslation;
        public string DeepLApiKey { get; set; } = string.Empty;
        public string AzureTranslatorKey { get; set; } = string.Empty;
        public string AzureTranslatorRegion { get; set; } = "global";
        public string GoogleCloudCredentialsPath { get; set; } = string.Empty;
        public string GoogleCloudProjectId { get; set; } = string.Empty;
        public string AzureDocumentIntelligenceEndpoint { get; set; } = string.Empty;
        public string AzureDocumentIntelligenceKey { get; set; } = string.Empty;
        public string TesseractDataPath { get; set; } = "./tessdata";
        public int CharactersTranslated { get; set; }
        public bool OpenPdfAfterTranslation { get; set; } = true;
    }
}

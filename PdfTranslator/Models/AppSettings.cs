namespace PdfTranslator.Models
{
    public enum PdfGeneratorEngine
    {
        PdfPig,      // Apache 2.0 - uses PdfSharpCore for overlay
        ITextSharp   // LGPLv2 - better rotation handling
    }

    public enum AccountTier
    {
        Free,
        Paid
    }

    public class AppSettings
    {
        public TranslationProvider SelectedTranslationProvider { get; set; } = TranslationProvider.MicrosoftTranslator;
        public OcrEngine SelectedOcrEngine { get; set; } = OcrEngine.Tesseract;
        public DeepLTranslationMode DeepLMode { get; set; } = DeepLTranslationMode.DocumentTranslation;
        public MicrosoftTranslationMode MicrosoftMode { get; set; } = MicrosoftTranslationMode.DocumentTranslation;
        public PdfGeneratorEngine SelectedPdfGenerator { get; set; } = PdfGeneratorEngine.PdfPig;
        
        // Account tier selections
        public AccountTier MicrosoftAccountTier { get; set; } = AccountTier.Free;
        public AccountTier DeepLAccountTier { get; set; } = AccountTier.Free;
        
        // DeepL API Keys (separate for Free and Paid)
        public string DeepLFreeApiKey { get; set; } = string.Empty;
        public string DeepLPaidApiKey { get; set; } = string.Empty;
        
        // Legacy - kept for backward compatibility, maps to Free tier
        public string DeepLApiKey { get; set; } = string.Empty;
        
        // Microsoft Free Tier (F0) - Text API only
        public string MicrosoftFreeApiKey { get; set; } = string.Empty;
        public string MicrosoftFreeRegion { get; set; } = "global";
        
        // Microsoft Paid Tier (S1) - Document Translation API
        public string MicrosoftPaidApiKey { get; set; } = string.Empty;
        public string MicrosoftPaidEndpoint { get; set; } = string.Empty;
        public string MicrosoftPaidStorageConnectionString { get; set; } = string.Empty;
        
        // Legacy - kept for backward compatibility
        public string AzureTranslatorKey { get; set; } = string.Empty;
        public string AzureTranslatorRegion { get; set; } = "global";
        public string AzureTranslatorEndpoint { get; set; } = string.Empty;
        public string AzureStorageConnectionString { get; set; } = string.Empty;
        
        public string GoogleCloudCredentialsPath { get; set; } = string.Empty;
        public string GoogleCloudProjectId { get; set; } = string.Empty;
        public string AzureDocumentIntelligenceEndpoint { get; set; } = string.Empty;
        public string AzureDocumentIntelligenceKey { get; set; } = string.Empty;
        public string TesseractDataPath { get; set; } = string.Empty;
        
        // Per-tier character tracking
        public int MicrosoftFreeCharactersTranslated { get; set; }
        public int MicrosoftPaidCharactersTranslated { get; set; }
        public int DeepLFreeCharactersTranslated { get; set; }
        public int DeepLPaidCharactersTranslated { get; set; }
        
        // Legacy - kept for backward compatibility (total of all)
        public int CharactersTranslated { get; set; }
        public bool OpenPdfAfterTranslation { get; set; } = true;
        
        /// <summary>
        /// Updates the character count for the current provider and tier.
        /// </summary>
        public void AddCharactersTranslated(int count, TranslationProvider provider, AccountTier tier)
        {
            CharactersTranslated += count; // Keep legacy total updated
            
            if (provider == TranslationProvider.MicrosoftTranslator)
            {
                if (tier == AccountTier.Free)
                    MicrosoftFreeCharactersTranslated += count;
                else
                    MicrosoftPaidCharactersTranslated += count;
            }
            else if (provider == TranslationProvider.DeepL)
            {
                if (tier == AccountTier.Free)
                    DeepLFreeCharactersTranslated += count;
                else
                    DeepLPaidCharactersTranslated += count;
            }
        }
        
        // Helper methods to get active credentials based on tier
        public string GetActiveDeepLApiKey() => DeepLAccountTier == AccountTier.Paid ? DeepLPaidApiKey : DeepLFreeApiKey;
        
        public string GetActiveMicrosoftApiKey() => MicrosoftAccountTier == AccountTier.Paid ? MicrosoftPaidApiKey : MicrosoftFreeApiKey;
        
        public string GetActiveMicrosoftRegion() => MicrosoftAccountTier == AccountTier.Paid ? string.Empty : MicrosoftFreeRegion;
        
        public string GetActiveMicrosoftEndpoint() => MicrosoftAccountTier == AccountTier.Paid ? MicrosoftPaidEndpoint : string.Empty;
        
        public string GetActiveMicrosoftStorageConnectionString() => MicrosoftAccountTier == AccountTier.Paid ? MicrosoftPaidStorageConnectionString : string.Empty;
        
        public bool IsMicrosoftDocumentTranslationAvailable() => MicrosoftAccountTier == AccountTier.Paid 
            && !string.IsNullOrWhiteSpace(MicrosoftPaidEndpoint) 
            && !string.IsNullOrWhiteSpace(MicrosoftPaidStorageConnectionString);
    }
}

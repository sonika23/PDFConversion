using PdfTranslator.Models;

namespace PdfTranslator.Services
{
    public class TranslationService
    {
        private readonly AppSettings _settings;
        private ITranslationProvider? _currentProvider;

        public TranslationService(AppSettings settings)
        {
            _settings = settings;
            InitializeProvider();
        }

        private void InitializeProvider()
        {
            _currentProvider = _settings.SelectedTranslationProvider switch
            {
                TranslationProvider.DeepL => new DeepLProvider(_settings.GetActiveDeepLApiKey()),
                TranslationProvider.MicrosoftTranslator => new MicrosoftTranslatorProvider(
                    _settings.GetActiveMicrosoftApiKey(), 
                    _settings.GetActiveMicrosoftRegion()),
                _ => null
            };
        }

        public void SwitchProvider(TranslationProvider provider)
        {
            _settings.SelectedTranslationProvider = provider;
            InitializeProvider();
        }

        public bool IsConfigured()
        {
            return _currentProvider?.IsConfigured() ?? false;
        }

        public async Task<TranslationResult> TranslateAsync(string text, string sourceLanguage = "ru", string targetLanguage = "en")
        {
            if (_currentProvider == null || !_currentProvider.IsConfigured())
            {
                throw new InvalidOperationException("Translation provider is not configured. Please set API keys in settings.");
            }

            var result = await _currentProvider.TranslateAsync(text, sourceLanguage, targetLanguage);
            
            // Update character count for the appropriate provider and tier
            UpdateCharacterCount(result.CharacterCount);
            
            return result;
        }

        public async Task<List<TranslationResult>> TranslateBatchAsync(List<string> texts, string sourceLanguage = "ru", string targetLanguage = "en")
        {
            if (_currentProvider == null || !_currentProvider.IsConfigured())
            {
                throw new InvalidOperationException("Translation provider is not configured. Please set API keys in settings.");
            }

            var results = await _currentProvider.TranslateBatchAsync(texts, sourceLanguage, targetLanguage);
            
            // Update character count for the appropriate provider and tier
            UpdateCharacterCount(results.Sum(r => r.CharacterCount));
            
            return results;
        }
        
        private void UpdateCharacterCount(int characterCount)
        {
            if (_settings.SelectedTranslationProvider == TranslationProvider.MicrosoftTranslator)
            {
                _settings.AddCharactersTranslated(characterCount, TranslationProvider.MicrosoftTranslator, _settings.MicrosoftAccountTier);
            }
            else if (_settings.SelectedTranslationProvider == TranslationProvider.DeepL)
            {
                _settings.AddCharactersTranslated(characterCount, TranslationProvider.DeepL, _settings.DeepLAccountTier);
            }
            else
            {
                // Google Cloud or other - just update legacy total
                _settings.CharactersTranslated += characterCount;
            }
        }

        public string GetProviderName()
        {
            return _settings.SelectedTranslationProvider.ToString();
        }
    }
}

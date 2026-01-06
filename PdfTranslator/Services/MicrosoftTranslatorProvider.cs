using PdfTranslator.Models;
using Azure;
using Azure.AI.Translation.Text;
using Serilog;

namespace PdfTranslator.Services
{
    public class MicrosoftTranslatorProvider : ITranslationProvider
    {
        private readonly string _apiKey;
        private readonly string _region;
        private TextTranslationClient? _client;

        public MicrosoftTranslatorProvider(string apiKey, string region = "global")
        {
            _apiKey = apiKey;
            _region = region;

            if (!string.IsNullOrEmpty(apiKey))
            {
                var credential = new AzureKeyCredential(apiKey);
                _client = new TextTranslationClient(credential, region: _region);
            }
        }

        public bool IsConfigured()
        {
            return !string.IsNullOrEmpty(_apiKey) && _client != null;
        }

        public async Task<TranslationResult> TranslateAsync(string text, string sourceLanguage = "ru", string targetLanguage = "en")
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("Azure Translator API key is not configured.");
            }

            try
            {
                var response = await _client!.TranslateAsync(
                    targetLanguage: targetLanguage,
                    content: new[] { text },
                    sourceLanguage: sourceLanguage);

                var translation = response.Value.FirstOrDefault();
                if (translation?.Translations?.FirstOrDefault() != null)
                {
                    var translatedText = translation.Translations.First().Text;
                    return new TranslationResult
                    {
                        OriginalText = text,
                        TranslatedText = translatedText,
                        CharacterCount = text.Length,
                        ConfidenceScore = 1.0
                    };
                }

                throw new Exception("No translation returned from Azure Translator.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Microsoft Translator failed for text: {Text}", text.Substring(0, Math.Min(50, text.Length)));
                throw;
            }
        }

        public async Task<List<TranslationResult>> TranslateBatchAsync(List<string> texts, string sourceLanguage = "ru", string targetLanguage = "en")
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("Azure Translator API key is not configured.");
            }

            var results = new List<TranslationResult>();

            try
            {
                var response = await _client!.TranslateAsync(
                    targetLanguage: targetLanguage,
                    content: texts,
                    sourceLanguage: sourceLanguage);

                int index = 0;
                foreach (var translation in response.Value)
                {
                    if (translation?.Translations?.FirstOrDefault() != null)
                    {
                        results.Add(new TranslationResult
                        {
                            OriginalText = texts[index],
                            TranslatedText = translation.Translations.First().Text,
                            CharacterCount = texts[index].Length,
                            ConfidenceScore = 1.0
                        });
                    }
                    index++;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Microsoft Translator batch translation failed");
                throw;
            }

            return results;
        }
    }
}

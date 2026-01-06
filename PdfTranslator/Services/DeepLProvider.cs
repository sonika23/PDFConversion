using System.IO;
using PdfTranslator.Models;
using DeepL;
using DeepL.Model;
using Serilog;

namespace PdfTranslator.Services
{
    public class DeepLProvider : ITranslationProvider
    {
        private readonly string _apiKey;
        private Translator? _translator;

        public DeepLProvider(string apiKey)
        {
            _apiKey = apiKey;
            if (!string.IsNullOrEmpty(apiKey))
            {
                _translator = new Translator(apiKey);
            }
        }

        public bool IsConfigured()
        {
            return !string.IsNullOrEmpty(_apiKey) && _translator != null;
        }

        /// <summary>
        /// Translates an entire document (PDF, DOCX, etc.) using DeepL's Document API.
        /// This preserves formatting much better than text-based translation.
        /// </summary>
        public async Task<bool> TranslateDocumentAsync(string inputPath, string outputPath, 
            string sourceLanguage = "RU", string targetLanguage = "EN-US")
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("DeepL API key is not configured.");
            }

            try
            {
                Log.Information("Starting DeepL document translation: {Input} -> {Output}", inputPath, outputPath);
                
                var inputFile = new FileInfo(inputPath);
                var outputFile = new FileInfo(outputPath);
                
                // Ensure output directory exists
                outputFile.Directory?.Create();
                
                // DeepL requires uppercase language codes
                string deepLSource = sourceLanguage.ToUpperInvariant();
                string deepLTarget = targetLanguage.ToUpperInvariant();
                if (deepLTarget == "EN") deepLTarget = "EN-US";
                
                await _translator!.TranslateDocumentAsync(
                    inputFile,
                    outputFile,
                    deepLSource,
                    deepLTarget);
                
                Log.Information("DeepL document translation completed: {Output}", outputPath);
                return true;
            }
            catch (DocumentTranslationException ex)
            {
                string errorDetail = $"DeepL Document Translation Error: {ex.Message}";
                if (ex.DocumentHandle != null)
                {
                    errorDetail += $"\nDocument Handle: {ex.DocumentHandle}";
                }
                if (ex.InnerException != null)
                {
                    errorDetail += $"\nInner Error: {ex.InnerException.Message}";
                }
                
                Log.Error(ex, "DeepL document translation failed. {ErrorDetail}", errorDetail);
                throw new InvalidOperationException(errorDetail, ex);
            }
            catch (DeepLException ex)
            {
                string errorDetail = $"DeepL API Error: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorDetail += $"\nInner Error: {ex.InnerException.Message}";
                }
                
                Log.Error(ex, "DeepL API error: {ErrorDetail}", errorDetail);
                throw new InvalidOperationException(errorDetail, ex);
            }
            catch (Exception ex)
            {
                string errorDetail = $"Document translation failed: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorDetail += $"\nInner Error: {ex.InnerException.Message}";
                }
                
                Log.Error(ex, "DeepL document translation failed for: {Input}. {ErrorDetail}", inputPath, errorDetail);
                throw new InvalidOperationException(errorDetail, ex);
            }
        }

        public async Task<TranslationResult> TranslateAsync(string text, string sourceLanguage = "ru", string targetLanguage = "EN-US")
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("DeepL API key is not configured.");
            }

            try
            {
                // DeepL requires specific language codes: EN-US for American English, EN-GB for British
                string deepLTarget = targetLanguage.ToUpperInvariant();
                if (deepLTarget == "EN") deepLTarget = "EN-US";
                
                var result = await _translator!.TranslateTextAsync(
                    text,
                    sourceLanguage.ToUpperInvariant(),
                    deepLTarget);

                return new TranslationResult
                {
                    OriginalText = text,
                    TranslatedText = result.Text,
                    CharacterCount = text.Length,
                    ConfidenceScore = 1.0 // DeepL doesn't provide confidence scores
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "DeepL translation failed for text: {Text}", text.Substring(0, Math.Min(50, text.Length)));
                throw;
            }
        }

        public async Task<List<TranslationResult>> TranslateBatchAsync(List<string> texts, string sourceLanguage = "ru", string targetLanguage = "EN-US")
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("DeepL API key is not configured.");
            }

            var results = new List<TranslationResult>();

            try
            {
                // DeepL requires specific language codes: EN-US for American English, EN-GB for British
                string deepLTarget = targetLanguage.ToUpperInvariant();
                if (deepLTarget == "EN") deepLTarget = "EN-US";
                
                // DeepL supports batch translation
                var translations = await _translator!.TranslateTextAsync(
                    texts.ToArray(),
                    sourceLanguage.ToUpperInvariant(),
                    deepLTarget);

                for (int i = 0; i < texts.Count; i++)
                {
                    results.Add(new TranslationResult
                    {
                        OriginalText = texts[i],
                        TranslatedText = translations[i].Text,
                        CharacterCount = texts[i].Length,
                        ConfidenceScore = 1.0
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "DeepL batch translation failed");
                throw;
            }

            return results;
        }
    }
}

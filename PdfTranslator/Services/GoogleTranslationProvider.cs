using System.IO;
using Google.Cloud.Translate.V3;
using Google.Protobuf;
using Google.Apis.Auth.OAuth2;
using Grpc.Auth;
using PdfTranslator.Models;
using Serilog;

namespace PdfTranslator.Services
{
    public class GoogleTranslationProvider : ITranslationProvider
    {
        private readonly string _credentialsPath;
        private readonly string _projectId;
        private TranslationServiceClient? _client;

        public GoogleTranslationProvider(string credentialsPath, string projectId)
        {
            _credentialsPath = credentialsPath;
            _projectId = projectId;
        }

        public bool IsConfigured()
        {
            return !string.IsNullOrEmpty(_credentialsPath) && 
                   !string.IsNullOrEmpty(_projectId) &&
                   File.Exists(_credentialsPath);
        }

        private TranslationServiceClient GetClient()
        {
            if (_client == null)
            {
                // Use Service Account credentials from JSON file
                var credential = GoogleCredential.FromFile(_credentialsPath)
                    .CreateScoped(TranslationServiceClient.DefaultScopes);
                
                var builder = new TranslationServiceClientBuilder
                {
                    Credential = credential
                };
                _client = builder.Build();
            }
            return _client;
        }

        /// <summary>
        /// Translates an entire document (PDF, DOCX, etc.) using Google's Document Translation API.
        /// This preserves formatting better than text-based translation.
        /// </summary>
        public async Task<int> TranslateDocumentAsync(string inputPath, string outputPath,
            string sourceLanguage = "ru", string targetLanguage = "en")
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("Google Cloud Translation is not configured. Please add your Service Account JSON file path and Project ID in Settings.");
            }

            try
            {
                Log.Information("Starting Google document translation: {Input} -> {Output}", inputPath, outputPath);

                var client = GetClient();
                var inputFile = new FileInfo(inputPath);
                var outputFile = new FileInfo(outputPath);

                // Ensure output directory exists
                outputFile.Directory?.Create();

                // Read the input file
                byte[] fileContent = await File.ReadAllBytesAsync(inputPath);
                int characterCount = 0;

                // Determine MIME type
                string mimeType = GetMimeType(inputPath);

                // Build the translation request
                var request = new TranslateDocumentRequest
                {
                    Parent = $"projects/{_projectId}/locations/global",
                    SourceLanguageCode = sourceLanguage.ToLowerInvariant(),
                    TargetLanguageCode = targetLanguage.ToLowerInvariant(),
                    DocumentInputConfig = new DocumentInputConfig
                    {
                        Content = ByteString.CopyFrom(fileContent),
                        MimeType = mimeType
                    }
                };

                Log.Information("Sending document to Google Cloud Translation API...");
                
                // Execute translation
                var response = await client.TranslateDocumentAsync(request);

                // Write the translated document
                if (response.DocumentTranslation?.ByteStreamOutputs?.Count > 0)
                {
                    using var outputStream = File.Create(outputPath);
                    foreach (var byteStream in response.DocumentTranslation.ByteStreamOutputs)
                    {
                        byteStream.WriteTo(outputStream);
                    }
                }
                else
                {
                    throw new InvalidOperationException("Google Translation API did not return translated document content.");
                }

                // Get character count from response if available
                // Estimate from input file if not provided
                characterCount = fileContent.Length / 2; // Rough estimate: 2 bytes per character for UTF-8 text

                Log.Information("Google document translation completed: {Output}", outputPath);
                return characterCount;
            }
            catch (Grpc.Core.RpcException ex)
            {
                string errorDetail = $"Google Cloud Translation Error: {ex.Status.Detail}";
                errorDetail += $"\nStatus Code: {ex.StatusCode}";
                if (ex.InnerException != null)
                {
                    errorDetail += $"\nInner Error: {ex.InnerException.Message}";
                }

                Log.Error(ex, "Google document translation failed. {ErrorDetail}", errorDetail);
                throw new InvalidOperationException(errorDetail, ex);
            }
            catch (Exception ex)
            {
                string errorDetail = $"Document translation failed: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorDetail += $"\nInner Error: {ex.InnerException.Message}";
                }

                Log.Error(ex, "Google document translation failed for: {Input}. {ErrorDetail}", inputPath, errorDetail);
                throw new InvalidOperationException(errorDetail, ex);
            }
        }

        private static string GetMimeType(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".html" => "text/html",
                ".htm" => "text/html",
                ".txt" => "text/plain",
                _ => "application/octet-stream"
            };
        }

        public async Task<TranslationResult> TranslateAsync(string text, string sourceLanguage = "ru", string targetLanguage = "en")
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("Google Cloud Translation is not configured. Please add your Service Account JSON file path and Project ID in Settings.");
            }

            try
            {
                var client = GetClient();

                var request = new TranslateTextRequest
                {
                    Parent = $"projects/{_projectId}/locations/global",
                    SourceLanguageCode = sourceLanguage.ToLowerInvariant(),
                    TargetLanguageCode = targetLanguage.ToLowerInvariant(),
                    Contents = { text }
                };

                var response = await client.TranslateTextAsync(request);

                return new TranslationResult
                {
                    OriginalText = text,
                    TranslatedText = response.Translations[0].TranslatedText,
                    CharacterCount = text.Length,
                    ConfidenceScore = 1.0 // Google doesn't provide confidence scores
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Google translation failed for text: {Text}", text.Substring(0, Math.Min(50, text.Length)));
                throw;
            }
        }

        public async Task<List<TranslationResult>> TranslateBatchAsync(List<string> texts, string sourceLanguage = "ru", string targetLanguage = "en")
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("Google Cloud Translation is not configured. Please add your Service Account JSON file path and Project ID in Settings.");
            }

            var results = new List<TranslationResult>();

            try
            {
                var client = GetClient();

                var request = new TranslateTextRequest
                {
                    Parent = $"projects/{_projectId}/locations/global",
                    SourceLanguageCode = sourceLanguage.ToLowerInvariant(),
                    TargetLanguageCode = targetLanguage.ToLowerInvariant(),
                };
                request.Contents.AddRange(texts);

                var response = await client.TranslateTextAsync(request);

                for (int i = 0; i < texts.Count && i < response.Translations.Count; i++)
                {
                    results.Add(new TranslationResult
                    {
                        OriginalText = texts[i],
                        TranslatedText = response.Translations[i].TranslatedText,
                        CharacterCount = texts[i].Length,
                        ConfidenceScore = 1.0
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Google batch translation failed");
                throw;
            }

            return results;
        }
    }
}

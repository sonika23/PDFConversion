using System.IO;
using PdfTranslator.Models;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Serilog;

namespace PdfTranslator.Services
{
    public class AzureDocumentIntelligenceEngine : IOcrEngine
    {
        private readonly string _endpoint;
        private readonly string _apiKey;
        private DocumentAnalysisClient? _client;

        public AzureDocumentIntelligenceEngine(string endpoint, string apiKey)
        {
            _endpoint = endpoint;
            _apiKey = apiKey;

            if (!string.IsNullOrEmpty(endpoint) && !string.IsNullOrEmpty(apiKey))
            {
                var credential = new AzureKeyCredential(apiKey);
                _client = new DocumentAnalysisClient(new Uri(endpoint), credential);
            }
        }

        public bool IsConfigured()
        {
            return !string.IsNullOrEmpty(_endpoint) && 
                   !string.IsNullOrEmpty(_apiKey) && 
                   _client != null;
        }

        public async Task<OcrResult> ExtractTextFromImageAsync(string imagePath)
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("Azure Document Intelligence is not configured.");
            }

            try
            {
                using var stream = File.OpenRead(imagePath);
                var operation = await _client!.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-read", stream);
                var result = operation.Value;

                var ocrResult = new OcrResult
                {
                    Text = result.Content,
                    Confidence = 1.0f // Azure provides page-level confidence
                };

                Log.Information("Azure Document Intelligence OCR completed. Extracted {Length} characters", result.Content.Length);
                return ocrResult;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Azure Document Intelligence OCR failed for image: {ImagePath}", imagePath);
                throw;
            }
        }

        public async Task<OcrResult> ExtractTextFromImageAsync(byte[] imageData)
        {
            if (!IsConfigured())
            {
                throw new InvalidOperationException("Azure Document Intelligence is not configured.");
            }

            try
            {
                using var stream = new MemoryStream(imageData);
                var operation = await _client!.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-read", stream);
                var result = operation.Value;

                var ocrResult = new OcrResult
                {
                    Text = result.Content,
                    Confidence = 1.0f
                };

                Log.Information("Azure Document Intelligence OCR completed. Extracted {Length} characters", result.Content.Length);
                return ocrResult;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Azure Document Intelligence OCR failed for image data");
                throw;
            }
        }
    }
}

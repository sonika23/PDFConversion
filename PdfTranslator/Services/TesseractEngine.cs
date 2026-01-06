using System.IO;
using PdfTranslator.Models;
using Serilog;
using TesseractOcr = Tesseract;

namespace PdfTranslator.Services
{
    public class TesseractOcrEngine : IOcrEngine
    {
        private readonly string _dataPath;
        private readonly string _language;

        public TesseractOcrEngine(string dataPath = "./tessdata", string language = "rus")
        {
            _dataPath = dataPath;
            _language = language;
        }

        public bool IsConfigured()
        {
            // Check if tessdata directory exists and contains the language file
            var langFile = Path.Combine(_dataPath, $"{_language}.traineddata");
            return Directory.Exists(_dataPath) && File.Exists(langFile);
        }

        public async Task<OcrResult> ExtractTextFromImageAsync(string imagePath)
        {
            return await Task.Run(() =>
            {
                if (!IsConfigured())
                {
                    throw new InvalidOperationException($"Tesseract is not configured. Missing language data at: {_dataPath}");
                }

                try
                {
                    using var engine = new TesseractOcr.TesseractEngine(_dataPath, _language, TesseractOcr.EngineMode.Default);
                    using var img = TesseractOcr.Pix.LoadFromFile(imagePath);
                    using var page = engine.Process(img);

                    var result = new OcrResult
                    {
                        Text = page.GetText(),
                        Confidence = page.GetMeanConfidence()
                    };

                    Log.Information("Tesseract OCR completed with confidence: {Confidence}", result.Confidence);
                    return result;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Tesseract OCR failed for image: {ImagePath}", imagePath);
                    throw;
                }
            });
        }

        public async Task<OcrResult> ExtractTextFromImageAsync(byte[] imageData)
        {
            return await Task.Run(() =>
            {
                if (!IsConfigured())
                {
                    throw new InvalidOperationException($"Tesseract is not configured. Missing language data at: {_dataPath}");
                }

                try
                {
                    using var engine = new TesseractOcr.TesseractEngine(_dataPath, _language, TesseractOcr.EngineMode.Default);
                    using var img = TesseractOcr.Pix.LoadFromMemory(imageData);
                    using var page = engine.Process(img);

                    var result = new OcrResult
                    {
                        Text = page.GetText(),
                        Confidence = page.GetMeanConfidence()
                    };

                    Log.Information("Tesseract OCR completed with confidence: {Confidence}", result.Confidence);
                    return result;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Tesseract OCR failed for image data");
                    throw;
                }
            });
        }
    }
}

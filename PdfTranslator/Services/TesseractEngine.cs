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
        private readonly bool _isConfigured;
        private readonly string? _configurationError;

        public TesseractOcrEngine(string? dataPath = null, string language = "rus")
        {
            _language = language;
            
            // If no path provided or relative path, look for tessdata in the application directory
            // This handles both empty/null and legacy "./tessdata" values
            if (string.IsNullOrEmpty(dataPath) || dataPath == "./tessdata" || !Path.IsPathRooted(dataPath))
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                _dataPath = Path.Combine(baseDir, "tessdata");
            }
            else
            {
                _dataPath = dataPath;
            }
            
            Log.Information("Tesseract looking for data at: {DataPath}", _dataPath);
            
            // Validate configuration at construction time
            var langFile = Path.Combine(_dataPath, $"{_language}.traineddata");
            
            if (!Directory.Exists(_dataPath))
            {
                _isConfigured = false;
                _configurationError = $"Tesseract data directory not found: {_dataPath}";
                Log.Error(_configurationError);
            }
            else if (!File.Exists(langFile))
            {
                _isConfigured = false;
                _configurationError = $"Tesseract language file not found: {langFile}. Please download '{_language}.traineddata' from https://github.com/tesseract-ocr/tessdata and place it in the tessdata folder.";
                Log.Error(_configurationError);
            }
            else
            {
                _isConfigured = true;
                _configurationError = null;
                Log.Information("Tesseract OCR configured successfully. Data path: {DataPath}, Language: {Language}", _dataPath, _language);
            }
        }

        public bool IsConfigured()
        {
            return _isConfigured;
        }
        
        /// <summary>
        /// Gets the configuration error message if Tesseract is not configured
        /// </summary>
        public string? GetConfigurationError()
        {
            return _configurationError;
        }
        
        /// <summary>
        /// Validates that Tesseract is configured and throws if not
        /// </summary>
        public void ValidateConfiguration()
        {
            if (!_isConfigured)
            {
                throw new InvalidOperationException(_configurationError ?? "Tesseract OCR is not configured properly.");
            }
        }

        public async Task<OcrResult> ExtractTextFromImageAsync(string imagePath)
        {
            return await Task.Run(() =>
            {
                ValidateConfiguration();

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
                ValidateConfiguration();

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

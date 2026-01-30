using System.IO;
using System.Drawing;
using PdfTranslator.Models;
using Serilog;

namespace PdfTranslator.Services
{
    public class PdfProcessingService
    {
        private readonly TranslationService _translationService;
        private readonly IOcrEngine _ocrEngine;
        private readonly AppSettings _settings;
        private readonly PdfTextExtractor _textExtractor;
        private readonly PdfToImageConverter _imageConverter;
        private readonly PdfOverlayGenerator _overlayGenerator;
        private readonly ITextSharpPdfOverlayGenerator _iTextSharpOverlayGenerator;
        private readonly ImagePreprocessor _imagePreprocessor;

        public event EventHandler<ProcessingProgress>? ProgressChanged;

        public PdfProcessingService(AppSettings settings)
        {
            _settings = settings;
            _translationService = new TranslationService(settings);
            _textExtractor = new PdfTextExtractor();
            _imageConverter = new PdfToImageConverter(Log.Logger);
            _overlayGenerator = new PdfOverlayGenerator(Log.Logger);
            _iTextSharpOverlayGenerator = new ITextSharpPdfOverlayGenerator(Log.Logger);
            _imagePreprocessor = new ImagePreprocessor();
            
            // Initialize OCR engine based on settings
            _ocrEngine = settings.SelectedOcrEngine switch
            {
                OcrEngine.Tesseract => new TesseractOcrEngine(settings.TesseractDataPath),
                OcrEngine.AzureDocumentIntelligence => new AzureDocumentIntelligenceEngine(
                    settings.AzureDocumentIntelligenceEndpoint,
                    settings.AzureDocumentIntelligenceKey),
                _ => new TesseractOcrEngine(settings.TesseractDataPath)
            };
        }

        public async Task<string> ProcessPdfAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Information("Starting PDF processing: {InputPath}", inputPath);
                ReportProgress(0, 0, "Analyzing PDF...");

                // Check if we should use DeepL Document API (preserves formatting best)
                if (_settings.SelectedTranslationProvider == TranslationProvider.DeepL && 
                    !string.IsNullOrEmpty(_settings.DeepLApiKey) &&
                    _settings.DeepLMode == DeepLTranslationMode.DocumentTranslation)
                {
                    return await ProcessWithDeepLDocumentApiAsync(inputPath, outputPath, cancellationToken);
                }

                // Check if we should use Microsoft Document Translation API
                if (_settings.SelectedTranslationProvider == TranslationProvider.MicrosoftTranslator &&
                    _settings.MicrosoftMode == MicrosoftTranslationMode.DocumentTranslation)
                {
                    return await ProcessWithMicrosoftDocumentApiAsync(inputPath, outputPath, cancellationToken);
                }

                // Check if we should use Google Cloud Document Translation
                if (_settings.SelectedTranslationProvider == TranslationProvider.GoogleCloud &&
                    !string.IsNullOrEmpty(_settings.GoogleCloudCredentialsPath) &&
                    !string.IsNullOrEmpty(_settings.GoogleCloudProjectId))
                {
                    return await ProcessWithGoogleDocumentApiAsync(inputPath, outputPath, cancellationToken);
                }

                // Use text extraction + overlay approach for Microsoft Translator Text mode or DeepL Text mode
                return await ProcessWithTextExtractionAsync(inputPath, outputPath, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PDF processing failed");
                throw;
            }
        }

        /// <summary>
        /// Uses Microsoft's Document Translation API with Azure Blob Storage.
        /// Preserves formatting natively and handles scanned PDFs with OCR.
        /// Pricing: ~$15 per million characters + storage costs.
        /// </summary>
        private async Task<string> ProcessWithMicrosoftDocumentApiAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
        {
            ReportProgress(0, 1, "Using Microsoft Document Translation...");
            
            var microsoftDocProvider = new MicrosoftDocumentTranslationProvider(_settings);
            
            var validation = microsoftDocProvider.ValidateSettings();
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(validation.ErrorMessage);
            }

            var progress = new Progress<string>(message => ReportProgress(0, 1, message));

            // Translate document and get character count
            var (translatedPath, characterCount) = await microsoftDocProvider.TranslateDocumentAsync(
                inputPath,
                "ru",    // Source: Russian
                "en",    // Target: English
                progress,
                cancellationToken);
            
            // Update character count for Microsoft Paid tier (Document API requires S1)
            _settings.AddCharactersTranslated(characterCount, TranslationProvider.MicrosoftTranslator, AccountTier.Paid);
            Log.Information("Updated Microsoft Paid characters translated: +{Added} = {Total}", 
                characterCount, _settings.MicrosoftPaidCharactersTranslated);
            
            ReportProgress(1, 1, "Completed!");
            Log.Information("Microsoft document translation completed: {OutputPath}", translatedPath);
            return translatedPath;
        }

        /// <summary>
        /// Uses Google Cloud's Document Translation API which preserves formatting natively.
        /// Has native OCR support for scanned documents.
        /// </summary>
        private async Task<string> ProcessWithGoogleDocumentApiAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
        {
            ReportProgress(0, 1, "Using Google Cloud Document Translation...");
            
            var googleProvider = new GoogleTranslationProvider(_settings.GoogleCloudCredentialsPath, _settings.GoogleCloudProjectId);
            
            if (!googleProvider.IsConfigured())
            {
                throw new InvalidOperationException("Google Cloud Translation is not configured. Please add your Service Account JSON file path and Project ID in Settings.");
            }

            ReportProgress(0, 1, "Uploading document to Google Cloud...");
            
            // Translate and get character count
            int characterCount = await googleProvider.TranslateDocumentAsync(
                inputPath, 
                outputPath, 
                "ru",    // Source: Russian
                "en");   // Target: English
            
            // Update character count (Google Cloud doesn't have tier separation, use legacy)
            _settings.CharactersTranslated += characterCount;
            Log.Information("Updated Google Cloud characters translated: +{Added} = {Total}", 
                characterCount, _settings.CharactersTranslated);
            
            ReportProgress(1, 1, "Completed!");
            Log.Information("Google document translation completed: {OutputPath}", outputPath);
            return outputPath;
        }

        /// <summary>
        /// Uses DeepL's Document Translation API which preserves formatting natively.
        /// This is the recommended approach for best quality output.
        /// </summary>
        private async Task<string> ProcessWithDeepLDocumentApiAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
        {
            ReportProgress(0, 1, "Using DeepL Document Translation (preserves formatting)...");
            
            var deepLProvider = new DeepLProvider(_settings.GetActiveDeepLApiKey());
            
            if (!deepLProvider.IsConfigured())
            {
                var tierName = _settings.DeepLAccountTier == AccountTier.Free ? "Free" : "Pro";
                throw new InvalidOperationException($"DeepL API key ({tierName}) is not configured. Please add your API key in Settings.");
            }

            // Extract text from PDF to count characters for tracking
            ReportProgress(0, 1, "Counting characters...");
            int characterCount = 0;
            try
            {
                var textBlocks = _textExtractor.ExtractTextBlocks(inputPath);
                characterCount = textBlocks.Sum(tb => tb.Text?.Length ?? 0);
                Log.Information("PDF contains approximately {CharCount} characters", characterCount);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not extract text to count characters, will estimate from file size");
                // Estimate: ~500 characters per KB for text-heavy PDFs
                var fileInfo = new System.IO.FileInfo(inputPath);
                characterCount = (int)(fileInfo.Length / 1024 * 500);
            }

            ReportProgress(0, 1, "Uploading document to DeepL...");
            
            // No silent fallback - let the error propagate to show the user what went wrong
            await deepLProvider.TranslateDocumentAsync(
                inputPath, 
                outputPath, 
                "RU",      // Source: Russian
                "EN-US");  // Target: American English
            
            // Update character count for the current DeepL tier
            _settings.AddCharactersTranslated(characterCount, TranslationProvider.DeepL, _settings.DeepLAccountTier);
            var deepLTierName = _settings.DeepLAccountTier == AccountTier.Free ? "Free" : "Pro";
            Log.Information("Updated DeepL {Tier} characters translated: +{Added}", deepLTierName, characterCount);
            
            ReportProgress(1, 1, "Completed!");
            Log.Information("DeepL document translation completed: {OutputPath}", outputPath);
            return outputPath;
        }

        /// <summary>
        /// Traditional approach: Extract text, translate, overlay on PDF.
        /// Used when DeepL Document API is not available.
        /// </summary>
        private async Task<string> ProcessWithTextExtractionAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
        {
            // Analyze PDF type
            bool isTextBased = PdfAnalyzer.IsTextBased(inputPath);
            int totalPages = PdfAnalyzer.GetPageCount(inputPath);

            ReportProgress(0, totalPages, $"Processing {totalPages} pages ({(isTextBased ? "text-based" : "scanned")})...");

            List<TextBlock> textBlocks;

            if (isTextBased)
            {
                // Extract text directly
                textBlocks = await ExtractTextFromPdfAsync(inputPath, totalPages, cancellationToken);
            }
            else
            {
                // Use OCR
                textBlocks = await ExtractTextWithOcrAsync(inputPath, totalPages, cancellationToken);
            }

            // Translate text blocks
            await TranslateTextBlocksAsync(textBlocks, totalPages, cancellationToken);

            // Generate output PDF
            ReportProgress(totalPages, totalPages, "Generating translated PDF...");
            await GenerateTranslatedPdfAsync(inputPath, outputPath, textBlocks, cancellationToken);

            ReportProgress(totalPages, totalPages, "Completed!");
            Log.Information("PDF processing completed: {OutputPath}", outputPath);

            return outputPath;
        }

        private async Task<List<TextBlock>> ExtractTextFromPdfAsync(string pdfPath, int totalPages, CancellationToken cancellationToken)
        {
            ReportProgress(0, totalPages, "Extracting text from PDF...");
            
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _textExtractor.ExtractTextBlocks(pdfPath);
            }, cancellationToken);
        }

        private async Task<List<TextBlock>> ExtractTextWithOcrAsync(string pdfPath, int totalPages, CancellationToken cancellationToken)
        {
            var textBlocks = new List<TextBlock>();

            ReportProgress(0, totalPages, "Converting PDF pages to images for OCR...");

            for (int pageNum = 0; pageNum < totalPages; pageNum++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    ReportProgress(pageNum, totalPages, $"Processing page {pageNum + 1}/{totalPages} with OCR...");

                    // Convert PDF page to image
                    using (var pageImage = _imageConverter.ConvertPageToImage(pdfPath, pageNum))
                    {
                        // Preprocess image for better OCR accuracy
                        using (var processedImage = _imagePreprocessor.Preprocess(pageImage))
                        {
                            // Save processed image to temp file for OCR
                            var tempPath = Path.Combine(Path.GetTempPath(), $"ocr_page_{Guid.NewGuid()}.png");
                            processedImage.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);

                            try
                            {
                                // Perform OCR
                                var ocrResult = await _ocrEngine.ExtractTextFromImageAsync(tempPath);

                                if (!string.IsNullOrWhiteSpace(ocrResult.Text))
                                {
                                    textBlocks.Add(new TextBlock
                                    {
                                        PageNumber = pageNum,
                                        OriginalText = ocrResult.Text,
                                        FontSize = 12, // Default font size
                                        X = 50, // Default position
                                        Y = 50
                                    });
                                }
                            }
                            finally
                            {
                                // Clean up temp file
                                if (File.Exists(tempPath))
                                {
                                    try { File.Delete(tempPath); } catch { }
                                }
                            }
                        }
                    }

                    Log.Information("Completed OCR for page {Page}/{Total}", pageNum + 1, totalPages);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "OCR failed for page {Page}", pageNum);
                    // Continue with next page
                }
            }

            return textBlocks;
        }

        private async Task TranslateTextBlocksAsync(List<TextBlock> textBlocks, int totalPages, CancellationToken cancellationToken)
        {
            if (!_translationService.IsConfigured())
            {
                throw new InvalidOperationException("Translation service is not configured. Please set API keys.");
            }

            // Group text blocks by page for progress reporting
            var blocksByPage = textBlocks.GroupBy(b => b.PageNumber).ToList();
            int processedPages = 0;

            foreach (var pageGroup in blocksByPage)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Batch translate all blocks on this page
                var textsToTranslate = pageGroup.Select(b => b.Text).ToList();
                
                ReportProgress(processedPages, totalPages, $"Translating page {pageGroup.Key}...");

                try
                {
                    var translations = await _translationService.TranslateBatchAsync(textsToTranslate);

                    // Update text blocks with translations
                    int index = 0;
                    foreach (var block in pageGroup)
                    {
                        if (index < translations.Count)
                        {
                            block.TranslatedText = translations[index].TranslatedText;
                        }
                        index++;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Translation failed for page {Page}", pageGroup.Key);
                    // Continue with remaining pages
                }

                processedPages++;
            }
        }

        private async Task GenerateTranslatedPdfAsync(string inputPath, string outputPath, List<TextBlock> textBlocks, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                Log.Information("Preparing PDF generation with {TotalBlocks} text blocks", textBlocks.Count);
                
                // Log sample of text blocks for debugging
                foreach (var block in textBlocks.Take(5))
                {
                    Log.Information("Sample block - Page: {Page}, Text: '{Text}', Translated: '{Translated}'", 
                        block.PageNumber, 
                        block.Text?.Substring(0, Math.Min(50, block.Text?.Length ?? 0)) ?? "(null)",
                        block.TranslatedText?.Substring(0, Math.Min(50, block.TranslatedText?.Length ?? 0)) ?? "(null)");
                }

                // Prepare translated text by page
                var translatedPageTexts = textBlocks
                    .Where(b => !string.IsNullOrEmpty(b.TranslatedText))
                    .GroupBy(b => b.PageNumber)
                    .ToDictionary(g => g.Key, g => string.Join("\n\n", g.Select(b => b.TranslatedText)));

                Log.Information("Generating PDF with {PageCount} translated pages, {BlockCount} text blocks, Page keys: {Keys}", 
                    translatedPageTexts.Count, textBlocks.Count, string.Join(", ", translatedPageTexts.Keys));

                // Now try to create PDF
                try
                {
                    // Check if we have blocks with position data for layout preservation
                    var blocksWithPositions = textBlocks
                        .Where(b => !string.IsNullOrEmpty(b.TranslatedText) && b.BoundingBox != null)
                        .ToList();

                    Log.Information("Blocks with position data: {Count}", blocksWithPositions.Count);

                    if (blocksWithPositions.Count > 0)
                    {
                        // Use position-preserving method (keeps original layout)
                        // Choose generator based on settings
                        string generatorName = _settings.SelectedPdfGenerator switch
                        {
                            PdfGeneratorEngine.ITextSharp => "iTextSharp (Overlay)",
                            _ => "PdfPig/PdfSharpCore (Overlay)"
                        };
                        Log.Information("Using {Generator} PDF generator", generatorName);
                        
                        try
                        {
                            if (_settings.SelectedPdfGenerator == PdfGeneratorEngine.ITextSharp)
                            {
                                _iTextSharpOverlayGenerator.CreateTranslatedPdf(inputPath, outputPath, textBlocks);
                            }
                            else
                            {
                                _overlayGenerator.CreateTranslatedPdf(inputPath, outputPath, textBlocks);
                            }
                            
                            // Verify the output
                            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 1000)
                            {
                                Log.Information("Created layout-preserved translated PDF at: {OutputPath}", outputPath);
                                return;
                            }
                            else
                            {
                                Log.Warning("Position-based PDF creation resulted in small/empty file, trying fallback");
                                if (File.Exists(outputPath)) File.Delete(outputPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Position-based overlay method failed, trying simple method");
                            if (File.Exists(outputPath)) File.Delete(outputPath);
                        }
                    }

                    // Fallback: Simple page-by-page translation (doesn't preserve exact positions)
                    if (translatedPageTexts.Count > 0)
                    {
                        try
                        {
                            _overlayGenerator.CreateSimpleTranslatedPdf(inputPath, outputPath, translatedPageTexts);
                            
                            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 1000)
                            {
                                Log.Information("Created simple translated PDF at: {OutputPath}", outputPath);
                                return;
                            }
                            else
                            {
                                Log.Warning("Simple PDF creation resulted in small/empty file, trying new PDF");
                                if (File.Exists(outputPath)) File.Delete(outputPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Simple method failed, trying new PDF method");
                            if (File.Exists(outputPath)) File.Delete(outputPath);
                        }

                        // Last resort: Create a brand new PDF with just the translations
                        _overlayGenerator.CreateNewPdfWithTranslation(outputPath, translatedPageTexts);
                        
                        if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 500)
                        {
                            Log.Information("Created new translated PDF at: {OutputPath}", outputPath);
                        }
                        else
                        {
                            Log.Error("Failed to create valid PDF output");
                        }
                    }
                    else
                    {
                        Log.Warning("No translations to write to PDF");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to generate translated PDF");
                    // Text file was already created as backup
                }
            }, cancellationToken);
        }

        private void ReportProgress(int current, int total, string status)
        {
            ProgressChanged?.Invoke(this, new ProcessingProgress
            {
                CurrentPage = current,
                TotalPages = total,
                Status = status
            });
        }

        public void SwitchOcrEngine(OcrEngine engine)
        {
            _settings.SelectedOcrEngine = engine;
            // Reinitialize would require recreating the service
        }

        public void SwitchTranslationProvider(TranslationProvider provider)
        {
            _translationService.SwitchProvider(provider);
        }
    }
}

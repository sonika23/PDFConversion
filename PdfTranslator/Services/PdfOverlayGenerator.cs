using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Drawing;
using PdfTranslator.Models;
using Serilog;

namespace PdfTranslator.Services
{
    /// <summary>
    /// Generates translated PDF files while preserving original layout including tables
    /// </summary>
    public class PdfOverlayGenerator
    {
        private readonly ILogger _logger;

        public PdfOverlayGenerator(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Creates a translated PDF that preserves the original layout exactly.
        /// Each text cell is replaced with its translation at the same position.
        /// </summary>
        public void CreateTranslatedPdf(string inputPdfPath, string outputPdfPath, List<TextBlock> translatedBlocks)
        {
            _logger.Information("Creating translated PDF with preserved layout: {Output}", outputPdfPath);

            if (translatedBlocks == null || translatedBlocks.Count == 0)
            {
                _logger.Warning("No translated blocks provided, copying original PDF");
                File.Copy(inputPdfPath, outputPdfPath, true);
                return;
            }

            try
            {
                // Open the source document for modification
                using (var document = PdfReader.Open(inputPdfPath, PdfDocumentOpenMode.Modify))
                {
                    // Group text blocks by page
                    var blocksByPage = translatedBlocks
                        .Where(b => !string.IsNullOrEmpty(b.TranslatedText) && b.BoundingBox != null)
                        .GroupBy(b => b.PageNumber)
                        .ToDictionary(g => g.Key, g => g.ToList());

                    _logger.Information("Processing {BlockCount} translated blocks across {PageCount} pages", 
                        translatedBlocks.Count(b => b.BoundingBox != null), blocksByPage.Count);

                    for (int pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
                    {
                        int pageNum1Based = pageIndex + 1;
                        var page = document.Pages[pageIndex];
                        
                        // Get page rotation and log for debugging
                        int rotation = page.Rotate;
                        
                        // PdfSharpCore's XGraphics works in the "user space" which accounts for rotation
                        // Use the page dimensions directly - XGraphics handles coordinate transformation
                        double pageHeight = page.Height.Point;
                        double pageWidth = page.Width.Point;
                        
                        _logger.Information("Page {Page}: Rotation={Rotation}°, Physical size: {Width}x{Height}",
                            pageNum1Based, rotation, pageWidth, pageHeight);

                        // Get blocks for this page (check both 0-based and 1-based)
                        List<TextBlock>? pageBlocks = null;
                        if (blocksByPage.TryGetValue(pageNum1Based, out var blocks1))
                        {
                            pageBlocks = blocks1;
                        }
                        else if (blocksByPage.TryGetValue(pageIndex, out var blocks0))
                        {
                            pageBlocks = blocks0;
                        }

                        if (pageBlocks == null || pageBlocks.Count == 0)
                        {
                            _logger.Information("Page {Page}: No blocks to translate", pageNum1Based);
                            continue;
                        }

                        _logger.Information("Page {Page}: Processing {Count} text cells", pageNum1Based, pageBlocks.Count);

                        // Create graphics object for this page - draw on top of existing content
                        using (var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
                        {
                            foreach (var block in pageBlocks)
                            {
                                if (block.BoundingBox == null || string.IsNullOrEmpty(block.TranslatedText))
                                    continue;

                                DrawTranslatedCell(gfx, block, pageHeight, pageWidth, rotation);
                            }
                        }
                    }

                    // Save the modified document
                    _logger.Information("Saving translated PDF to: {Output}", outputPdfPath);
                    document.Save(outputPdfPath);
                    _logger.Information("PDF saved successfully");
                }

                VerifyOutput(outputPdfPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create translated PDF with overlay method");
                
                // Fallback to simple method
                _logger.Information("Falling back to simple PDF generation");
                var translatedPageTexts = translatedBlocks
                    .Where(b => !string.IsNullOrEmpty(b.TranslatedText))
                    .GroupBy(b => b.PageNumber)
                    .ToDictionary(g => g.Key, g => string.Join(" ", g.Select(b => b.TranslatedText)));
                
                CreateSimpleTranslatedPdf(inputPdfPath, outputPdfPath, translatedPageTexts);
            }
        }

        /// <summary>
        /// Draws a translated text cell at the exact position of the original.
        /// Preserves font size as closely as possible to the source document.
        /// </summary>
        private void DrawTranslatedCell(XGraphics gfx, TextBlock block, double pageHeight, double pageWidth, int rotation = 0)
        {
            var box = block.BoundingBox!;
            
            // Both PdfPig and PdfSharpCore XGraphics work in the "visual" coordinate space
            // (the page as you see it when viewing). The only difference is:
            // - PdfPig: origin at bottom-left, Y increases upward
            // - XGraphics: origin at top-left, Y increases downward
            // So we just need to flip the Y-axis for ALL pages, regardless of rotation.
            
            // Simple Y-axis flip: convert from bottom-left to top-left origin
            double drawX = box.X;
            double drawY = pageHeight - box.Y - box.Height;
            double drawWidth = box.Width;
            double drawHeight = box.Height;

            // Add padding to white box to fully cover original text
            double padding = 3;
            double whiteBoxX = Math.Max(0, drawX - padding);
            double whiteBoxY = Math.Max(0, drawY - padding);
            double whiteBoxWidth = Math.Min(drawWidth + (2 * padding), pageWidth - whiteBoxX);
            double whiteBoxHeight = drawHeight + (2 * padding);

            // Draw white rectangle to cover original text
            gfx.DrawRectangle(XBrushes.White, whiteBoxX, whiteBoxY, whiteBoxWidth, whiteBoxHeight);

            string translatedText = block.TranslatedText ?? "";
            if (string.IsNullOrWhiteSpace(translatedText))
                return;

            // FONT SIZE STRATEGY:
            // 1. Use the original document's font size as the PRIMARY source
            // 2. Only reduce font size as a LAST RESORT, and never below 7pt (readable minimum)
            // 3. If text doesn't fit, prefer truncation over unreadable tiny fonts
            
            double originalFontSize = block.FontSize;
            
            // Validate original font size - if it seems unreasonable, calculate from bounding box
            if (originalFontSize <= 0 || originalFontSize > 72)
            {
                // Estimate font size from cell height (typical font height is ~70-80% of point size)
                originalFontSize = box.Height * 0.85;
            }
            
            // Ensure font size is within readable bounds
            // Minimum 7pt for readability, maximum 24pt for normal document text
            double fontSize = Math.Max(7, Math.Min(originalFontSize, 24));
            
            // Round to nearest 0.5pt for consistency
            fontSize = Math.Round(fontSize * 2) / 2;

            // Determine font style based on source document analysis
            XFontStyle fontStyle = XFontStyle.Regular;
            if (block.IsBold && block.IsItalic)
                fontStyle = XFontStyle.BoldItalic;
            else if (block.IsBold)
                fontStyle = XFontStyle.Bold;
            else if (block.IsItalic)
                fontStyle = XFontStyle.Italic;

            // Use font family from source document, fall back to Arial
            string fontFamily = !string.IsNullOrEmpty(block.FontFamily) ? block.FontFamily : "Arial";
            
            XFont font = new XFont(fontFamily, fontSize, fontStyle);
            XSize textSize = gfx.MeasureString(translatedText, font);

            // Check if text fits in the available width (using transformed dimensions)
            double availableWidth = drawWidth;
            
            if (textSize.Width > availableWidth && availableWidth > 20)
            {
                // Text doesn't fit - try to reduce font size but NOT below 7pt
                double requiredRatio = availableWidth / textSize.Width;
                double reducedFontSize = fontSize * requiredRatio * 0.95; // 5% margin
                
                // CRITICAL: Never go below 7pt - prefer truncation over unreadable text
                if (reducedFontSize >= 7)
                {
                    fontSize = Math.Round(reducedFontSize * 2) / 2; // Round to 0.5pt
                    font = new XFont(fontFamily, fontSize, fontStyle);
                }
                else
                {
                    // Text won't fit even at 7pt - use 7pt and let it overflow slightly
                    // This is better than unreadable tiny text
                    fontSize = 7;
                    font = new XFont(fontFamily, fontSize, fontStyle);
                    
                    // Optionally truncate very long text that won't fit
                    textSize = gfx.MeasureString(translatedText, font);
                    if (textSize.Width > availableWidth * 1.5) // Allow 50% overflow max
                    {
                        translatedText = TruncateTextToFit(gfx, translatedText, font, availableWidth * 1.3);
                    }
                }
            }

            // Calculate text position using transformed coordinates
            // Horizontal: Left-aligned at transformed position
            double textX = drawX;
            
            // Vertical: Center the text vertically in the cell
            double textY = drawY + (drawHeight + fontSize * 0.75) / 2;
            
            // Use detected text color from source (default is black: 0,0,0)
            XBrush textBrush = new XSolidBrush(XColor.FromArgb(
                block.TextColorR, 
                block.TextColorG, 
                block.TextColorB));
            
            gfx.DrawString(translatedText, font, textBrush, textX, textY);
        }

        /// <summary>
        /// Truncates text to fit within specified width, adding ellipsis
        /// </summary>
        private string TruncateTextToFit(XGraphics gfx, string text, XFont font, double maxWidth)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            string ellipsis = "...";
            XSize ellipsisSize = gfx.MeasureString(ellipsis, font);
            double targetWidth = maxWidth - ellipsisSize.Width;

            if (targetWidth <= 0)
                return ellipsis;

            // Binary search for optimal truncation point
            int left = 0;
            int right = text.Length;
            
            while (left < right)
            {
                int mid = (left + right + 1) / 2;
                string testText = text.Substring(0, mid);
                XSize size = gfx.MeasureString(testText, font);
                
                if (size.Width <= targetWidth)
                    left = mid;
                else
                    right = mid - 1;
            }

            if (left < text.Length)
                return text.Substring(0, left) + ellipsis;
            
            return text;
        }

        /// <summary>
        /// Creates a simpler translated PDF - replaces each page with translated text
        /// </summary>
        public void CreateSimpleTranslatedPdf(string inputPdfPath, string outputPdfPath, 
            Dictionary<int, string> translatedPageTexts)
        {
            _logger.Information("Creating simple translated PDF: {Output}", outputPdfPath);

            if (translatedPageTexts == null || translatedPageTexts.Count == 0)
            {
                _logger.Warning("No translated text provided, copying original PDF");
                File.Copy(inputPdfPath, outputPdfPath, true);
                return;
            }

            try
            {
                using (var inputDocument = PdfReader.Open(inputPdfPath, PdfDocumentOpenMode.Import))
                using (var outputDocument = new PdfDocument())
                {
                    outputDocument.Info.Title = "Translated Document";
                    outputDocument.Info.Author = "PDF Translator";

                    for (int pageIndex = 0; pageIndex < inputDocument.PageCount; pageIndex++)
                    {
                        int pageNum = pageIndex + 1;
                        var originalPage = inputDocument.Pages[pageIndex];
                        
                        var newPage = outputDocument.AddPage();
                        newPage.Width = originalPage.Width;
                        newPage.Height = originalPage.Height;

                        string? translatedText = null;
                        if (translatedPageTexts.TryGetValue(pageNum, out var text1))
                            translatedText = text1;
                        else if (translatedPageTexts.TryGetValue(pageIndex, out var text0))
                            translatedText = text0;

                        using (var gfx = XGraphics.FromPdfPage(newPage))
                        {
                            gfx.DrawRectangle(XBrushes.White, 0, 0, newPage.Width.Point, newPage.Height.Point);

                            if (!string.IsNullOrWhiteSpace(translatedText))
                            {
                                DrawWrappedText(gfx, translatedText, newPage.Width.Point, newPage.Height.Point);
                            }
                        }
                    }

                    outputDocument.Save(outputPdfPath);
                }

                VerifyOutput(outputPdfPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create simple translated PDF");
                throw;
            }
        }

        /// <summary>
        /// Creates a new PDF with translations (fallback method)
        /// </summary>
        public void CreateNewPdfWithTranslation(string outputPdfPath, Dictionary<int, string> translatedPageTexts, 
            double pageWidth = 595, double pageHeight = 842)
        {
            _logger.Information("Creating new PDF with translations: {Output}", outputPdfPath);

            if (translatedPageTexts == null || translatedPageTexts.Count == 0)
            {
                _logger.Warning("No translated text provided");
                return;
            }

            try
            {
                using (var document = new PdfDocument())
                {
                    document.Info.Title = "Translated Document";
                    document.Info.Author = "PDF Translator";

                    foreach (var kvp in translatedPageTexts.OrderBy(k => k.Key))
                    {
                        var page = document.AddPage();
                        page.Width = XUnit.FromPoint(pageWidth);
                        page.Height = XUnit.FromPoint(pageHeight);

                        using (var gfx = XGraphics.FromPdfPage(page))
                        {
                            gfx.DrawRectangle(XBrushes.White, 0, 0, pageWidth, pageHeight);

                            if (!string.IsNullOrWhiteSpace(kvp.Value))
                            {
                                DrawWrappedText(gfx, kvp.Value, pageWidth, pageHeight);
                            }
                        }
                    }

                    document.Save(outputPdfPath);
                }

                VerifyOutput(outputPdfPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create new PDF");
                throw;
            }
        }

        private void DrawWrappedText(XGraphics gfx, string text, double pageWidth, double pageHeight)
        {
            double margin = 50;
            double y = margin;
            double maxWidth = pageWidth - (2 * margin);
            var font = new XFont("Arial", 11, XFontStyle.Regular);
            double lineHeight = font.Height * 1.2;

            var words = text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var currentLine = "";

            foreach (var word in words)
            {
                var testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                var size = gfx.MeasureString(testLine, font);

                if (size.Width > maxWidth && !string.IsNullOrEmpty(currentLine))
                {
                    gfx.DrawString(currentLine, font, XBrushes.Black, margin, y);
                    y += lineHeight;
                    currentLine = word;

                    if (y > pageHeight - margin)
                        break;
                }
                else
                {
                    currentLine = testLine;
                }
            }

            if (!string.IsNullOrEmpty(currentLine) && y <= pageHeight - margin)
            {
                gfx.DrawString(currentLine, font, XBrushes.Black, margin, y);
            }
        }

        private void VerifyOutput(string outputPdfPath)
        {
            if (File.Exists(outputPdfPath))
            {
                var fileInfo = new FileInfo(outputPdfPath);
                _logger.Information("Output PDF created: {Size} bytes", fileInfo.Length);
                
                if (fileInfo.Length < 1000)
                {
                    _logger.Warning("Output PDF may be corrupted (size: {Size} bytes)", fileInfo.Length);
                }
            }
            else
            {
                _logger.Error("Output PDF was not created!");
            }
        }
    }
}

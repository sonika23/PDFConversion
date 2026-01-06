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
    /// Generates translated PDF files while preserving original layout including tables.
    /// Uses a simple coordinate transformation approach.
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
                        
                        // Get page dimensions
                        double pageWidth = page.Width.Point;
                        double pageHeight = page.Height.Point;
                        
                        _logger.Information("Page {Page}: Size={W}x{H}", pageNum1Based, pageWidth, pageHeight);

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
                            int drawnCount = 0;
                            int skippedCount = 0;
                            
                            foreach (var block in pageBlocks)
                            {
                                if (block.BoundingBox == null || string.IsNullOrEmpty(block.TranslatedText))
                                {
                                    skippedCount++;
                                    continue;
                                }

                                // Draw cell using simple coordinate conversion
                                DrawTranslatedCell(gfx, block, pageHeight);
                                drawnCount++;
                            }
                            
                            _logger.Information("Page {Page}: Drew {Drawn} cells, skipped {Skipped} cells", 
                                pageNum1Based, drawnCount, skippedCount);
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
        /// PdfPig coordinates: bottom-left origin, Y increases upward
        /// XGraphics coordinates: top-left origin, Y increases downward
        /// </summary>
        private void DrawTranslatedCell(XGraphics gfx, TextBlock block, double pageHeight)
        {
            var box = block.BoundingBox!;
            
            // Convert from PdfPig coordinates (bottom-left origin) to XGraphics coordinates (top-left origin)
            // PdfPig: Y is distance from bottom of page
            // XGraphics: Y is distance from top of page
            // So: xgraphics_Y = pageHeight - pdfpig_Y - box_height
            
            double drawX = box.X;
            double drawY = pageHeight - box.Y - box.Height;
            double drawWidth = box.Width;
            double drawHeight = box.Height;

            // Add padding to white box to fully cover original text
            double padding = 2;
            double whiteBoxX = Math.Max(0, drawX - padding);
            double whiteBoxY = Math.Max(0, drawY - padding);
            double whiteBoxWidth = drawWidth + (2 * padding);
            double whiteBoxHeight = drawHeight + (2 * padding);

            // Draw white rectangle to cover original text
            gfx.DrawRectangle(XBrushes.White, whiteBoxX, whiteBoxY, whiteBoxWidth, whiteBoxHeight);

            string translatedText = block.TranslatedText ?? "";
            if (string.IsNullOrWhiteSpace(translatedText))
                return;

            // Font size - use original or estimate from cell height
            double originalFontSize = block.FontSize;
            if (originalFontSize <= 0 || originalFontSize > 72)
            {
                originalFontSize = drawHeight * 0.85;
            }
            double fontSize = Math.Max(6, Math.Min(originalFontSize, 24));

            // Font style
            XFontStyle fontStyle = XFontStyle.Regular;
            if (block.IsBold && block.IsItalic)
                fontStyle = XFontStyle.BoldItalic;
            else if (block.IsBold)
                fontStyle = XFontStyle.Bold;
            else if (block.IsItalic)
                fontStyle = XFontStyle.Italic;

            string fontFamily = "Arial";
            XFont font;
            try
            {
                font = new XFont(fontFamily, fontSize, fontStyle);
            }
            catch
            {
                font = new XFont("Arial", fontSize, XFontStyle.Regular);
            }
            
            // Adjust font size if text doesn't fit
            XSize textSize = gfx.MeasureString(translatedText, font);
            if (textSize.Width > drawWidth && drawWidth > 20)
            {
                double requiredRatio = drawWidth / textSize.Width;
                double reducedFontSize = fontSize * requiredRatio * 0.95;
                if (reducedFontSize >= 6)
                {
                    fontSize = reducedFontSize;
                    try
                    {
                        font = new XFont(fontFamily, fontSize, fontStyle);
                    }
                    catch
                    {
                        font = new XFont("Arial", fontSize, XFontStyle.Regular);
                    }
                }
            }
            
            // Text color
            XBrush textBrush = new XSolidBrush(XColor.FromArgb(
                block.TextColorR, block.TextColorG, block.TextColorB));

            // Draw text - position text at the top-left of the cell, vertically offset for baseline
            double textX = drawX;
            double textY = drawY + fontSize; // Move down by font size since DrawString uses baseline
            
            gfx.DrawString(translatedText, font, textBrush, textX, textY);
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

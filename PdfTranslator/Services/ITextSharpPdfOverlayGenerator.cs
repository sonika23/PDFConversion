using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using iTextSharp.text;
using iTextSharp.text.pdf;
using PdfTranslator.Models;
using Serilog;

namespace PdfTranslator.Services
{
    /// <summary>
    /// PDF overlay generator using iTextSharp (LGPLv2).
    /// Handles rotated and landscape PDFs natively.
    /// </summary>
    public class ITextSharpPdfOverlayGenerator
    {
        private readonly ILogger _logger;

        public ITextSharpPdfOverlayGenerator(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Creates a translated PDF that preserves the original layout exactly.
        /// Uses iTextSharp's native rotation handling for better landscape/rotated PDF support.
        /// </summary>
        public void CreateTranslatedPdf(string inputPdfPath, string outputPdfPath, List<TextBlock> translatedBlocks)
        {
            _logger.Information("[iTextSharp] Creating translated PDF with preserved layout: {Output}", outputPdfPath);

            if (translatedBlocks == null || translatedBlocks.Count == 0)
            {
                _logger.Warning("[iTextSharp] No translated blocks provided, copying original PDF");
                File.Copy(inputPdfPath, outputPdfPath, true);
                return;
            }

            try
            {
                // Group text blocks by page
                var blocksByPage = translatedBlocks
                    .Where(b => !string.IsNullOrEmpty(b.TranslatedText) && b.BoundingBox != null)
                    .GroupBy(b => b.PageNumber)
                    .ToDictionary(g => g.Key, g => g.ToList());

                _logger.Information("[iTextSharp] Processing {BlockCount} translated blocks across {PageCount} pages",
                    translatedBlocks.Count(b => b.BoundingBox != null), blocksByPage.Count);

                using (var reader = new PdfReader(inputPdfPath))
                using (var fileStream = new FileStream(outputPdfPath, FileMode.Create, FileAccess.Write))
                using (var stamper = new PdfStamper(reader, fileStream))
                {
                    for (int pageIndex = 0; pageIndex < reader.NumberOfPages; pageIndex++)
                    {
                        int pageNum1Based = pageIndex + 1;

                        // Get page dimensions - iTextSharp handles rotation automatically
                        var pageSize = reader.GetPageSizeWithRotation(pageNum1Based);
                        var pageSizeRaw = reader.GetPageSize(pageNum1Based);
                        int rotation = reader.GetPageRotation(pageNum1Based);

                        float pageWidth = pageSize.Width;
                        float pageHeight = pageSize.Height;

                        _logger.Information("[iTextSharp] Page {Page}: Size={W}x{H}, Rotation={R}°, Raw={RW}x{RH}",
                            pageNum1Based, pageWidth, pageHeight, rotation, pageSizeRaw.Width, pageSizeRaw.Height);

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
                            _logger.Information("[iTextSharp] Page {Page}: No blocks to translate", pageNum1Based);
                            continue;
                        }

                        _logger.Information("[iTextSharp] Page {Page}: Processing {Count} text cells", pageNum1Based, pageBlocks.Count);

                        // Get the content layer to draw on top of existing content
                        var canvas = stamper.GetOverContent(pageNum1Based);

                        int drawnCount = 0;
                        int skippedCount = 0;

                        foreach (var block in pageBlocks)
                        {
                            if (block.BoundingBox == null || string.IsNullOrEmpty(block.TranslatedText))
                            {
                                skippedCount++;
                                continue;
                            }

                            // Draw cell using iTextSharp - coordinates from PdfPig are in PDF space (bottom-left origin)
                            DrawTranslatedCell(canvas, block, pageWidth, pageHeight, rotation);
                            drawnCount++;
                        }

                        _logger.Information("[iTextSharp] Page {Page}: Drew {Drawn} cells, skipped {Skipped} cells",
                            pageNum1Based, drawnCount, skippedCount);
                    }
                }

                VerifyOutput(outputPdfPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[iTextSharp] Failed to create translated PDF");

                // Fallback to simple method
                _logger.Information("[iTextSharp] Falling back to simple PDF generation");
                var translatedPageTexts = translatedBlocks
                    .Where(b => !string.IsNullOrEmpty(b.TranslatedText))
                    .GroupBy(b => b.PageNumber)
                    .ToDictionary(g => g.Key, g => string.Join(" ", g.Select(b => b.TranslatedText)));

                CreateSimpleTranslatedPdf(inputPdfPath, outputPdfPath, translatedPageTexts);
            }
        }

        /// <summary>
        /// Draws a translated text cell at the exact position of the original.
        /// iTextSharp uses PDF coordinate system (bottom-left origin, Y up) - same as PdfPig!
        /// </summary>
        private void DrawTranslatedCell(PdfContentByte canvas, TextBlock block,
            float pageWidth, float pageHeight, int rotation)
        {
            var box = block.BoundingBox!;

            // PdfPig and iTextSharp both use PDF coordinates (bottom-left origin)
            // So we can use the coordinates directly!
            float drawX = (float)box.X;
            float drawY = (float)box.Y;
            float drawWidth = (float)box.Width;
            float drawHeight = (float)box.Height;

            // For rotated pages, we may need to transform coordinates
            // PdfPig extracts in visual space, but iTextSharp draws in physical space
            if (rotation == 90)
            {
                // Rotated 90° CCW: visual (x,y) -> physical (y, pageWidth-x-width)
                float newX = drawY;
                float newY = pageWidth - drawX - drawWidth;
                drawX = newX;
                drawY = newY;
                // Swap dimensions
                float temp = drawWidth;
                drawWidth = drawHeight;
                drawHeight = temp;
            }
            else if (rotation == 270)
            {
                // Rotated 90° CW: visual (x,y) -> physical (pageHeight-y-height, x)
                float newX = pageHeight - drawY - drawHeight;
                float newY = drawX;
                drawX = newX;
                drawY = newY;
                // Swap dimensions
                float temp = drawWidth;
                drawWidth = drawHeight;
                drawHeight = temp;
            }
            else if (rotation == 180)
            {
                // Rotated 180°: visual (x,y) -> physical (pageWidth-x-width, pageHeight-y-height)
                drawX = pageWidth - drawX - drawWidth;
                drawY = pageHeight - drawY - drawHeight;
            }

            // Add padding to white box
            float padding = 2f;
            float whiteBoxX = Math.Max(0, drawX - padding);
            float whiteBoxY = Math.Max(0, drawY - padding);
            float whiteBoxWidth = drawWidth + (2 * padding);
            float whiteBoxHeight = drawHeight + (2 * padding);

            // Draw white rectangle to cover original text
            canvas.SaveState();
            canvas.SetColorFill(BaseColor.White);
            canvas.Rectangle(whiteBoxX, whiteBoxY, whiteBoxWidth, whiteBoxHeight);
            canvas.Fill();
            canvas.RestoreState();

            string translatedText = block.TranslatedText ?? "";
            if (string.IsNullOrWhiteSpace(translatedText))
                return;

            // Font size - use original or estimate from cell height
            float originalFontSize = (float)block.FontSize;
            if (originalFontSize <= 0 || originalFontSize > 72)
            {
                originalFontSize = drawHeight * 0.85f;
            }
            float fontSize = Math.Max(6f, Math.Min(originalFontSize, 24f));

            // Create font
            BaseFont baseFont;
            try
            {
                // Try to use Arial (Helvetica in PDF)
                baseFont = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.CP1252, BaseFont.NOT_EMBEDDED);
            }
            catch
            {
                baseFont = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.CP1252, BaseFont.NOT_EMBEDDED);
            }

            // Adjust font size if text doesn't fit
            float textWidth = baseFont.GetWidthPoint(translatedText, fontSize);
            if (textWidth > drawWidth && drawWidth > 20)
            {
                float requiredRatio = drawWidth / textWidth;
                float reducedFontSize = fontSize * requiredRatio * 0.95f;
                if (reducedFontSize >= 6f)
                {
                    fontSize = reducedFontSize;
                }
            }

            // Text color
            canvas.SaveState();
            canvas.SetColorFill(new BaseColor(block.TextColorR, block.TextColorG, block.TextColorB));

            // Begin text
            canvas.BeginText();
            canvas.SetFontAndSize(baseFont, fontSize);

            // Position text at bottom-left of cell, offset by font ascent
            float textX = drawX;
            float textY = drawY + (drawHeight - fontSize) / 2 + fontSize * 0.2f; // Vertically center

            canvas.SetTextMatrix(textX, textY);
            canvas.ShowText(translatedText);
            canvas.EndText();
            canvas.RestoreState();
        }

        /// <summary>
        /// Creates a simpler translated PDF - replaces each page with translated text
        /// </summary>
        public void CreateSimpleTranslatedPdf(string inputPdfPath, string outputPdfPath,
            Dictionary<int, string> translatedPageTexts)
        {
            _logger.Information("[iTextSharp] Creating simple translated PDF: {Output}", outputPdfPath);

            if (translatedPageTexts == null || translatedPageTexts.Count == 0)
            {
                _logger.Warning("[iTextSharp] No translated text provided, copying original PDF");
                File.Copy(inputPdfPath, outputPdfPath, true);
                return;
            }

            try
            {
                using (var reader = new PdfReader(inputPdfPath))
                {
                    var document = new Document();
                    using (var fileStream = new FileStream(outputPdfPath, FileMode.Create))
                    using (var writer = PdfWriter.GetInstance(document, fileStream))
                    {
                        document.Open();

                        var font = FontFactory.GetFont(FontFactory.HELVETICA, 11, BaseColor.Black);

                        for (int pageIndex = 0; pageIndex < reader.NumberOfPages; pageIndex++)
                        {
                            int pageNum = pageIndex + 1;
                            var originalPageSize = reader.GetPageSizeWithRotation(pageNum);

                            document.SetPageSize(originalPageSize);
                            document.NewPage();

                            string? translatedText = null;
                            if (translatedPageTexts.TryGetValue(pageNum, out var text1))
                                translatedText = text1;
                            else if (translatedPageTexts.TryGetValue(pageIndex, out var text0))
                                translatedText = text0;

                            if (!string.IsNullOrWhiteSpace(translatedText))
                            {
                                var paragraph = new Paragraph(translatedText, font);
                                paragraph.Alignment = Element.ALIGN_LEFT;
                                document.Add(paragraph);
                            }
                        }

                        document.Close();
                    }
                }

                VerifyOutput(outputPdfPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[iTextSharp] Failed to create simple translated PDF");
                throw;
            }
        }

        /// <summary>
        /// Creates a new PDF with translations (fallback method)
        /// </summary>
        public void CreateNewPdfWithTranslation(string outputPdfPath, Dictionary<int, string> translatedPageTexts,
            double pageWidth = 595, double pageHeight = 842)
        {
            _logger.Information("[iTextSharp] Creating new PDF with translations: {Output}", outputPdfPath);

            if (translatedPageTexts == null || translatedPageTexts.Count == 0)
            {
                _logger.Warning("[iTextSharp] No translated text provided");
                return;
            }

            try
            {
                var document = new Document(new iTextSharp.text.Rectangle((float)pageWidth, (float)pageHeight));
                using (var fileStream = new FileStream(outputPdfPath, FileMode.Create))
                using (var writer = PdfWriter.GetInstance(document, fileStream))
                {
                    document.Open();

                    var font = FontFactory.GetFont(FontFactory.HELVETICA, 11, BaseColor.Black);

                    foreach (var kvp in translatedPageTexts.OrderBy(k => k.Key))
                    {
                        document.NewPage();

                        if (!string.IsNullOrWhiteSpace(kvp.Value))
                        {
                            var paragraph = new Paragraph(kvp.Value, font);
                            paragraph.Alignment = Element.ALIGN_LEFT;
                            document.Add(paragraph);
                        }
                    }

                    document.Close();
                }

                VerifyOutput(outputPdfPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[iTextSharp] Failed to create new PDF");
                throw;
            }
        }

        private void VerifyOutput(string outputPdfPath)
        {
            if (File.Exists(outputPdfPath))
            {
                var fileInfo = new FileInfo(outputPdfPath);
                _logger.Information("[iTextSharp] Output PDF created: {Size} bytes", fileInfo.Length);

                if (fileInfo.Length < 1000)
                {
                    _logger.Warning("[iTextSharp] Output PDF may be corrupted (size: {Size} bytes)", fileInfo.Length);
                }
            }
            else
            {
                _logger.Error("[iTextSharp] Output PDF was not created!");
            }
        }
    }
}

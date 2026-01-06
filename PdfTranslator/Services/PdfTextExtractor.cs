using PdfTranslator.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Serilog;

namespace PdfTranslator.Services
{
    public class PdfTextExtractor
    {
        /// <summary>
        /// Extracts text blocks from PDF, preserving exact positions for table layouts.
        /// Groups words into logical cells/segments based on horizontal gaps.
        /// </summary>
        public List<TextBlock> ExtractTextBlocks(string pdfPath)
        {
            var textBlocks = new List<TextBlock>();

            try
            {
                using var document = PdfDocument.Open(pdfPath);
                
                for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
                {
                    var page = document.GetPage(pageNum);
                    var words = page.GetWords().ToList();
                    
                    if (words.Count == 0)
                        continue;

                    // Group words into cells - words that are close horizontally on the same line
                    var cells = GroupWordsIntoCells(words);
                    
                    foreach (var cell in cells)
                    {
                        if (cell.Words.Count == 0)
                            continue;

                        // Combine words in the cell
                        var combinedText = string.Join(" ", cell.Words.Select(w => w.Text));
                        
                        // Get font size - use the most common (mode) font size in the cell
                        var fontSizes = cell.Words
                            .SelectMany(w => w.Letters)
                            .Where(l => l.PointSize > 0)
                            .Select(l => Math.Round(l.PointSize * 2) / 2) // Round to 0.5pt
                            .ToList();
                        
                        double fontSize;
                        if (fontSizes.Count > 0)
                        {
                            fontSize = fontSizes
                                .GroupBy(s => s)
                                .OrderByDescending(g => g.Count())
                                .First()
                                .Key;
                        }
                        else
                        {
                            fontSize = cell.Height * 0.85;
                        }
                        
                        if (fontSize < 5 || fontSize > 72)
                        {
                            fontSize = cell.Height * 0.85;
                        }

                        // Extract font style information from font names
                        var fontNames = cell.Words
                            .Select(w => w.FontName ?? "")
                            .Where(f => !string.IsNullOrEmpty(f))
                            .ToList();
                        
                        string primaryFontName = fontNames.FirstOrDefault() ?? "Arial";
                        
                        // Detect bold and italic from font name
                        // Font names often contain: Bold, Italic, BoldItalic, Oblique, Heavy, Black, Light, etc.
                        var (isBold, isItalic, fontFamily) = AnalyzeFontName(primaryFontName);
                        
                        // Also check if majority of letters indicate bold based on font name patterns
                        if (!isBold)
                        {
                            int boldCount = fontNames.Count(f => IsBoldFont(f));
                            isBold = boldCount > fontNames.Count / 2;
                        }
                        
                        if (!isItalic)
                        {
                            int italicCount = fontNames.Count(f => IsItalicFont(f));
                            isItalic = italicCount > fontNames.Count / 2;
                        }

                        textBlocks.Add(new TextBlock
                        {
                            Text = combinedText,
                            BoundingBox = new BoundingBox(cell.X, cell.Y, cell.Width, cell.Height),
                            FontName = primaryFontName,
                            FontFamily = fontFamily,
                            FontSize = fontSize,
                            IsBold = isBold,
                            IsItalic = isItalic,
                            PageNumber = pageNum,
                            X = cell.X,
                            Y = cell.Y
                        });
                    }
                }

                Log.Information("Extracted {Count} text cells from PDF", textBlocks.Count);
                
                // Log samples
                foreach (var block in textBlocks.Take(5))
                {
                    Log.Debug("Cell: Page {Page}, Pos ({X:F1}, {Y:F1}), Size {W:F1}x{H:F1}, Text: '{Text}'",
                        block.PageNumber, block.X, block.Y, 
                        block.BoundingBox?.Width ?? 0, block.BoundingBox?.Height ?? 0,
                        block.Text?.Length > 40 ? block.Text.Substring(0, 40) + "..." : block.Text);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to extract text from PDF: {Path}", pdfPath);
                throw;
            }

            return textBlocks;
        }

        /// <summary>
        /// Groups words into cells based on proximity.
        /// Words on the same line that are close together form a cell.
        /// Large horizontal gaps indicate column boundaries.
        /// </summary>
        private List<TextCell> GroupWordsIntoCells(List<Word> words)
        {
            if (words.Count == 0)
                return new List<TextCell>();

            var cells = new List<TextCell>();

            // First, group words by similar Y position (same row)
            var rows = GroupWordsIntoRows(words);

            foreach (var rowWords in rows)
            {
                // Within each row, group words that are close together horizontally (same cell)
                var rowCells = GroupRowIntoCells(rowWords);
                cells.AddRange(rowCells);
            }

            return cells;
        }

        /// <summary>
        /// Groups words into rows based on Y position
        /// </summary>
        private List<List<Word>> GroupWordsIntoRows(List<Word> words)
        {
            // Sort by Y (descending - top to bottom in visual terms)
            var sortedWords = words.OrderByDescending(w => w.BoundingBox.Bottom).ToList();
            
            var rows = new List<List<Word>>();
            var currentRow = new List<Word> { sortedWords[0] };
            double currentY = sortedWords[0].BoundingBox.Bottom;
            
            // Tolerance for same row - words within 30% of average height
            double avgHeight = sortedWords.Average(w => w.BoundingBox.Height);
            double yTolerance = avgHeight * 0.4;

            for (int i = 1; i < sortedWords.Count; i++)
            {
                var word = sortedWords[i];
                
                if (Math.Abs(word.BoundingBox.Bottom - currentY) <= yTolerance)
                {
                    currentRow.Add(word);
                }
                else
                {
                    rows.Add(currentRow.OrderBy(w => w.BoundingBox.Left).ToList());
                    currentRow = new List<Word> { word };
                    currentY = word.BoundingBox.Bottom;
                }
            }

            if (currentRow.Count > 0)
            {
                rows.Add(currentRow.OrderBy(w => w.BoundingBox.Left).ToList());
            }

            return rows;
        }

        /// <summary>
        /// Within a row, groups words into cells based on horizontal gaps.
        /// Large gaps indicate column boundaries.
        /// </summary>
        private List<TextCell> GroupRowIntoCells(List<Word> rowWords)
        {
            if (rowWords.Count == 0)
                return new List<TextCell>();

            // Sort by X position
            var sorted = rowWords.OrderBy(w => w.BoundingBox.Left).ToList();
            
            var cells = new List<TextCell>();
            var currentCell = new TextCell();
            currentCell.Words.Add(sorted[0]);

            // Calculate average character width to determine gap threshold
            double avgCharWidth = sorted.Average(w => w.BoundingBox.Width / Math.Max(1, w.Text.Length));
            double gapThreshold = avgCharWidth * 2.5; // Gap of 2.5+ characters = new cell

            for (int i = 1; i < sorted.Count; i++)
            {
                var prevWord = sorted[i - 1];
                var currWord = sorted[i];
                
                // Calculate gap between words
                double gap = currWord.BoundingBox.Left - prevWord.BoundingBox.Right;

                if (gap > gapThreshold)
                {
                    // Large gap - start new cell
                    currentCell.CalculateBounds();
                    cells.Add(currentCell);
                    currentCell = new TextCell();
                }

                currentCell.Words.Add(currWord);
            }

            // Don't forget the last cell
            if (currentCell.Words.Count > 0)
            {
                currentCell.CalculateBounds();
                cells.Add(currentCell);
            }

            return cells;
        }

        public List<string> ExtractTextByParagraph(string pdfPath)
        {
            var paragraphs = new List<string>();

            try
            {
                using var document = PdfDocument.Open(pdfPath);
                
                foreach (var page in document.GetPages())
                {
                    var text = page.Text;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var pageParagraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                        paragraphs.AddRange(pageParagraphs.Select(p => p.Trim()));
                    }
                }

                Log.Information("Extracted {Count} paragraphs from PDF", paragraphs.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to extract paragraphs from PDF: {Path}", pdfPath);
                throw;
            }

            return paragraphs;
        }

        /// <summary>
        /// Analyzes a font name to extract style information (bold, italic) and base family
        /// </summary>
        private (bool isBold, bool isItalic, string fontFamily) AnalyzeFontName(string fontName)
        {
            if (string.IsNullOrEmpty(fontName))
                return (false, false, "Arial");

            string upperName = fontName.ToUpperInvariant();
            
            bool isBold = IsBoldFont(fontName);
            bool isItalic = IsItalicFont(fontName);
            
            // Extract base font family by removing style indicators
            string family = fontName;
            
            // Common patterns to remove from font names to get base family
            string[] styleIndicators = { 
                "BoldItalic", "BoldOblique", "Bold", "Italic", "Oblique",
                "Heavy", "Black", "ExtraBold", "SemiBold", "DemiBold",
                "Light", "Thin", "Medium", "Regular", "Normal",
                "-", "_", ",", "MT", "PS"
            };
            
            foreach (var indicator in styleIndicators)
            {
                family = family.Replace(indicator, "", StringComparison.OrdinalIgnoreCase);
            }
            
            family = family.Trim();
            
            // Map common PDF font names to system fonts
            family = MapToSystemFont(family, fontName);
            
            return (isBold, isItalic, family);
        }

        /// <summary>
        /// Detects if a font name indicates bold weight
        /// </summary>
        private static bool IsBoldFont(string fontName)
        {
            if (string.IsNullOrEmpty(fontName))
                return false;
                
            string upper = fontName.ToUpperInvariant();
            
            return upper.Contains("BOLD") || 
                   upper.Contains("BLACK") || 
                   upper.Contains("HEAVY") ||
                   upper.Contains("EXTRABOLD") ||
                   upper.Contains("SEMIBOLD") ||
                   upper.Contains("DEMIBOLD") ||
                   upper.Contains("ULTRA") ||
                   // Some fonts use weight numbers: 700+ is typically bold
                   upper.Contains("700") ||
                   upper.Contains("800") ||
                   upper.Contains("900");
        }

        /// <summary>
        /// Detects if a font name indicates italic style
        /// </summary>
        private static bool IsItalicFont(string fontName)
        {
            if (string.IsNullOrEmpty(fontName))
                return false;
                
            string upper = fontName.ToUpperInvariant();
            
            return upper.Contains("ITALIC") || 
                   upper.Contains("OBLIQUE") ||
                   upper.Contains("INCLINED") ||
                   upper.Contains("SLANTED");
        }

        /// <summary>
        /// Maps PDF embedded font names to common system fonts
        /// </summary>
        private static string MapToSystemFont(string extractedFamily, string originalFontName)
        {
            string upper = originalFontName.ToUpperInvariant();
            
            // Arial family
            if (upper.Contains("ARIAL") || upper.Contains("HELVETICA"))
                return "Arial";
            
            // Times family
            if (upper.Contains("TIMES") || upper.Contains("TIMESNEWROMAN"))
                return "Times New Roman";
            
            // Calibri family
            if (upper.Contains("CALIBRI"))
                return "Calibri";
            
            // Courier family
            if (upper.Contains("COURIER"))
                return "Courier New";
            
            // Verdana family
            if (upper.Contains("VERDANA"))
                return "Verdana";
            
            // Tahoma family
            if (upper.Contains("TAHOMA"))
                return "Tahoma";
            
            // Georgia family
            if (upper.Contains("GEORGIA"))
                return "Georgia";
            
            // Default to Arial for unknown fonts
            if (string.IsNullOrWhiteSpace(extractedFamily))
                return "Arial";
            
            return extractedFamily;
        }

        /// <summary>
        /// Helper class to represent a cell (group of words)
        /// </summary>
        private class TextCell
        {
            public List<Word> Words { get; } = new List<Word>();
            public double X { get; private set; }
            public double Y { get; private set; }
            public double Width { get; private set; }
            public double Height { get; private set; }

            public void CalculateBounds()
            {
                if (Words.Count == 0)
                    return;

                X = Words.Min(w => w.BoundingBox.Left);
                Y = Words.Min(w => w.BoundingBox.Bottom);
                double maxX = Words.Max(w => w.BoundingBox.Right);
                double maxY = Words.Max(w => w.BoundingBox.Top);
                Width = maxX - X;
                Height = maxY - Y;
            }
        }
    }
}

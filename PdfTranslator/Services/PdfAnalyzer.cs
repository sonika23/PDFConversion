using PdfTranslator.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Serilog;

namespace PdfTranslator.Services
{
    public class PdfAnalyzer
    {
        public static bool IsTextBased(string pdfPath)
        {
            try
            {
                using var document = PdfDocument.Open(pdfPath);
                
                // Check first few pages
                int pagesToCheck = Math.Min(3, document.NumberOfPages);
                int textPageCount = 0;

                for (int i = 1; i <= pagesToCheck; i++)
                {
                    var page = document.GetPage(i);
                    var text = page.Text?.Trim() ?? string.Empty;
                    
                    // If page has reasonable amount of extractable text, it's text-based
                    if (text.Length > 50)
                    {
                        textPageCount++;
                    }
                }

                bool isTextBased = textPageCount > pagesToCheck / 2;
                Log.Information("PDF analyzed: {IsTextBased} (text pages: {TextPages}/{Total})", 
                    isTextBased ? "Text-based" : "Scanned", textPageCount, pagesToCheck);
                
                return isTextBased;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to analyze PDF: {Path}", pdfPath);
                throw;
            }
        }

        public static int GetPageCount(string pdfPath)
        {
            try
            {
                using var document = PdfDocument.Open(pdfPath);
                return document.NumberOfPages;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get page count for PDF: {Path}", pdfPath);
                throw;
            }
        }
    }
}

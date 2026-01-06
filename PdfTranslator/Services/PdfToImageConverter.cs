using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using PdfiumViewer;
using Serilog;

namespace PdfTranslator.Services
{
    /// <summary>
    /// Converts PDF pages to images for OCR processing
    /// </summary>
    public class PdfToImageConverter
    {
        private readonly ILogger _logger;
        private const int DefaultDpi = 300; // High DPI for better OCR accuracy

        public PdfToImageConverter(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Converts a PDF page to an image
        /// </summary>
        /// <param name="pdfPath">Path to the PDF file</param>
        /// <param name="pageNumber">Page number (0-based)</param>
        /// <param name="dpi">Resolution in DPI (default: 300)</param>
        /// <returns>Bitmap image of the page</returns>
        public Bitmap ConvertPageToImage(string pdfPath, int pageNumber, int dpi = DefaultDpi)
        {
            try
            {
                _logger.Information("Converting PDF page {Page} to image at {Dpi} DPI", pageNumber, dpi);

                using (var document = PdfDocument.Load(pdfPath))
                {
                    if (pageNumber >= document.PageCount)
                    {
                        throw new ArgumentOutOfRangeException(nameof(pageNumber), 
                            $"Page {pageNumber} does not exist. Document has {document.PageCount} pages.");
                    }

                    // Render the page to an image
                    var image = document.Render(pageNumber, dpi, dpi, PdfRenderFlags.CorrectFromDpi);
                    
                    _logger.Debug("Successfully rendered page {Page} ({Width}x{Height})", 
                        pageNumber, image.Width, image.Height);

                    return (Bitmap)image;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to convert PDF page {Page} to image", pageNumber);
                throw;
            }
        }

        /// <summary>
        /// Converts all pages in a PDF to images
        /// </summary>
        /// <param name="pdfPath">Path to the PDF file</param>
        /// <param name="dpi">Resolution in DPI (default: 300)</param>
        /// <returns>List of bitmap images, one per page</returns>
        public List<Bitmap> ConvertAllPagesToImages(string pdfPath, int dpi = DefaultDpi)
        {
            var images = new List<Bitmap>();

            try
            {
                using (var document = PdfDocument.Load(pdfPath))
                {
                    _logger.Information("Converting {PageCount} pages to images", document.PageCount);

                    for (int i = 0; i < document.PageCount; i++)
                    {
                        var image = document.Render(i, dpi, dpi, PdfRenderFlags.CorrectFromDpi);
                        images.Add((Bitmap)image);
                        
                        _logger.Debug("Rendered page {Page}/{Total}", i + 1, document.PageCount);
                    }

                    return images;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to convert PDF pages to images");
                
                // Clean up any images we created before the error
                foreach (var image in images)
                {
                    image?.Dispose();
                }
                
                throw;
            }
        }

        /// <summary>
        /// Converts a PDF page to a temporary image file
        /// </summary>
        /// <param name="pdfPath">Path to the PDF file</param>
        /// <param name="pageNumber">Page number (0-based)</param>
        /// <param name="dpi">Resolution in DPI (default: 300)</param>
        /// <returns>Path to the temporary image file</returns>
        public string ConvertPageToTempImageFile(string pdfPath, int pageNumber, int dpi = DefaultDpi)
        {
            try
            {
                using (var image = ConvertPageToImage(pdfPath, pageNumber, dpi))
                {
                    var tempPath = Path.Combine(Path.GetTempPath(), $"pdf_page_{Guid.NewGuid()}.png");
                    image.Save(tempPath, ImageFormat.Png);
                    
                    _logger.Debug("Saved page {Page} to temporary file: {Path}", pageNumber, tempPath);
                    return tempPath;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save PDF page {Page} to temp file", pageNumber);
                throw;
            }
        }

        /// <summary>
        /// Gets the number of pages in a PDF
        /// </summary>
        public int GetPageCount(string pdfPath)
        {
            try
            {
                using (var document = PdfDocument.Load(pdfPath))
                {
                    return document.PageCount;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get page count from PDF");
                throw;
            }
        }
    }
}

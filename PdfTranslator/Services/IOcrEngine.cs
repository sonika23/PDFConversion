using PdfTranslator.Models;

namespace PdfTranslator.Services
{
    public interface IOcrEngine
    {
        Task<OcrResult> ExtractTextFromImageAsync(string imagePath);
        Task<OcrResult> ExtractTextFromImageAsync(byte[] imageData);
        bool IsConfigured();
    }
}

using OpenCvSharp;

namespace PdfTranslator.Models
{
    public class OcrResult
    {
        public string Text { get; set; } = string.Empty;
        public float Confidence { get; set; }
        public Rect BoundingBox { get; set; }
        public List<TextBlock> TextBlocks { get; set; } = new();
    }
}

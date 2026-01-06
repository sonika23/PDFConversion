namespace PdfTranslator.Models
{
    public class TextBlock
    {
        public string OriginalText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
        public BoundingBox? BoundingBox { get; set; }
        public string FontName { get; set; } = string.Empty;
        public double FontSize { get; set; }
        public int PageNumber { get; set; }
        
        // Position properties for canvas drawing
        public double X { get; set; }
        public double Y { get; set; }

        // Font style properties - extracted from source PDF
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public string FontFamily { get; set; } = "Arial"; // Base font family without style modifiers
        
        // Text color (RGB values 0-255)
        public int TextColorR { get; set; } = 0;
        public int TextColorG { get; set; } = 0;
        public int TextColorB { get; set; } = 0;

        // Legacy property for backwards compatibility
        public string Text
        {
            get => OriginalText;
            set => OriginalText = value;
        }
    }
}

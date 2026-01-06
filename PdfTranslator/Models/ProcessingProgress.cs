namespace PdfTranslator.Models
{
    public class ProcessingProgress
    {
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public string Status { get; set; } = string.Empty;
        public int PercentComplete => TotalPages > 0 ? (CurrentPage * 100) / TotalPages : 0;
    }
}

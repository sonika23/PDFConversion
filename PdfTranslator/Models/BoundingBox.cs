namespace PdfTranslator.Models
{
    /// <summary>
    /// Represents a rectangular bounding box for text positioning
    /// </summary>
    public class BoundingBox
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public BoundingBox() { }

        public BoundingBox(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }
}

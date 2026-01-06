using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using OpenCvSharp;
using Serilog;

namespace PdfTranslator.Services
{
    public class ImagePreprocessor
    {
        public Bitmap Preprocess(Bitmap bitmap)
        {
            try
            {
                // Convert Bitmap to byte array
                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms, ImageFormat.Png);
                    var imageData = ms.ToArray();
                    
                    // Process the image
                    var processedData = PreprocessForOcr(imageData);
                    
                    // Convert back to Bitmap
                    using (var ms2 = new MemoryStream(processedData))
                    {
                        return new Bitmap(ms2);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Image preprocessing failed, returning original image");
                return (Bitmap)bitmap.Clone();
            }
        }

        public static byte[] PreprocessForOcr(byte[] imageData)
        {
            try
            {
                using var mat = Mat.FromImageData(imageData);
                
                // Convert to grayscale
                using var gray = new Mat();
                Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);

                // Apply deskewing
                var deskewed = DeskewImage(gray);

                // Apply binarization using Otsu's method
                using var binary = new Mat();
                Cv2.Threshold(deskewed, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

                // Apply noise removal
                using var denoised = new Mat();
                Cv2.MedianBlur(binary, denoised, 3);

                // Enhance contrast
                using var enhanced = new Mat();
                Cv2.EqualizeHist(denoised, enhanced);

                // Convert back to bytes
                return enhanced.ToBytes(".png");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Image preprocessing failed, returning original image");
                return imageData;
            }
        }

        private static Mat DeskewImage(Mat image)
        {
            try
            {
                // Find all non-zero points
                var points = new List<OpenCvSharp.Point>();
                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        if (image.At<byte>(y, x) > 0)
                        {
                            points.Add(new OpenCvSharp.Point(x, y));
                        }
                    }
                }

                if (points.Count < 5)
                    return image.Clone();

                // Get minimum area rectangle
                var rect = Cv2.MinAreaRect(points);
                var angle = rect.Angle;

                // Adjust angle
                if (angle < -45)
                    angle += 90;

                // Only deskew if angle is significant
                if (Math.Abs(angle) < 0.5)
                    return image.Clone();

                // Rotate image
                var center = new Point2f(image.Width / 2f, image.Height / 2f);
                var rotationMatrix = Cv2.GetRotationMatrix2D(center, angle, 1.0);
                var rotated = new Mat();
                Cv2.WarpAffine(image, rotated, rotationMatrix, image.Size(), InterpolationFlags.Cubic);

                Log.Information("Image deskewed by {Angle} degrees", angle);
                return rotated;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Deskewing failed, returning original image");
                return image.Clone();
            }
        }
    }
}

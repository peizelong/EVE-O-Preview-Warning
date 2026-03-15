using System.Drawing;
using System.Drawing.Imaging;

namespace EVEAutoWarning.Core;

public class RedDetector
{
    public int RedThreshold { get; set; } = 150;
    public int MinRedRatio { get; set; } = 120;
    public int MinSaturation { get; set; } = 50;
    public int DetectionPixelCount { get; set; } = 100;
    public int SampleStep { get; set; } = 4;

    public DetectionResult Detect(Bitmap bitmap, Rectangle region = default)
    {
        Rectangle scanRegion = region == default ? new Rectangle(0, 0, bitmap.Width, bitmap.Height) : region;
        
        int redPixelCount = 0;
        int totalScannedPixels = 0;
        List<Point> redPixels = new();

        BitmapData bmpData = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            int startX = Math.Max(0, scanRegion.X);
            int startY = Math.Max(0, scanRegion.Y);
            int endX = Math.Min(bitmap.Width, scanRegion.X + scanRegion.Width);
            int endY = Math.Min(bitmap.Height, scanRegion.Y + scanRegion.Height);

            unsafe
            {
                byte* ptr = (byte*)bmpData.Scan0;
                int bytes = Math.Abs(bmpData.Stride) * bitmap.Height;

                for (int y = startY; y < endY; y += SampleStep)
                {
                    for (int x = startX; x < endX; x += SampleStep)
                    {
                        int offset = y * bmpData.Stride + x * 4;
                        
                        byte b = ptr[offset];
                        byte g = ptr[offset + 1];
                        byte r = ptr[offset + 2];

                        totalScannedPixels++;

                        if (IsRedPixel(r, g, b))
                        {
                            redPixelCount++;
                            redPixels.Add(new Point(x, y));
                        }
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(bmpData);
        }

        double redPercentage = totalScannedPixels > 0 
            ? (double)redPixelCount / totalScannedPixels * 100 
            : 0;

        bool isAlert = redPixelCount >= DetectionPixelCount;

        return new DetectionResult
        {
            IsAlert = isAlert,
            RedPixelCount = redPixelCount,
            TotalScannedPixels = totalScannedPixels,
            RedPercentage = redPercentage,
            RedPixelLocations = redPixels
        };
    }

    private bool IsRedPixel(byte r, byte g, byte b)
    {
        if (r < RedThreshold)
            return false;

        int maxOther = Math.Max(g, b);
        if (r <= maxOther + MinRedRatio)
            return false;

        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));
        int saturation = max > 0 ? (max - min) * 100 / max : 0;
        
        if (saturation < MinSaturation)
            return false;

        return true;
    }

    public static Rectangle GetDefaultScanRegion()
    {
        int screenWidth = Screen.PrimaryScreen.Bounds.Width;
        int screenHeight = Screen.PrimaryScreen.Bounds.Height;
        
        return new Rectangle(
            screenWidth / 4,
            screenHeight / 4,
            screenWidth / 2,
            screenHeight / 2
        );
    }
}

public class DetectionResult
{
    public bool IsAlert { get; set; }
    public int RedPixelCount { get; set; }
    public int TotalScannedPixels { get; set; }
    public double RedPercentage { get; set; }
    public List<Point> RedPixelLocations { get; set; } = new();
}

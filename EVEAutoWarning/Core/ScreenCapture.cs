using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace EVEAutoWarning.Core;

public class ScreenCapture
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hDestDC, int x, int y, int nWidth, int nHeight, 
        IntPtr hSrcDC, int xSrc, int ySrc, int dwRop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    private const int SRCCOPY = 0x00CC0020;

    public Bitmap CaptureScreen()
    {
        int screenWidth = Screen.PrimaryScreen.Bounds.Width;
        int screenHeight = Screen.PrimaryScreen.Bounds.Height;
        return CaptureRegion(0, 0, screenWidth, screenHeight);
    }

    public Bitmap CaptureRegion(int x, int y, int width, int height)
    {
        IntPtr hDesktop = GetDesktopWindow();
        IntPtr hDC = GetWindowDC(hDesktop);
        IntPtr hMemDC = CreateCompatibleDC(hDC);
        IntPtr hBitmap = CreateCompatibleBitmap(hDC, width, height);
        IntPtr hOldBitmap = SelectObject(hMemDC, hBitmap);

        BitBlt(hMemDC, 0, 0, width, height, hDC, x, y, SRCCOPY);

        SelectObject(hMemDC, hOldBitmap);
        DeleteDC(hMemDC);
        ReleaseDC(hDesktop, hDC);

        Bitmap bitmap = Image.FromHbitmap(hBitmap);
        DeleteObject(hBitmap);

        return bitmap;
    }

    public Bitmap CaptureRegion(Rectangle region)
    {
        return CaptureRegion(region.X, region.Y, region.Width, region.Height);
    }

    public Color GetPixelColor(Bitmap bitmap, int x, int y)
    {
        if (x < 0 || x >= bitmap.Width || y < 0 || y >= bitmap.Height)
            return Color.Empty;
        
        return bitmap.GetPixel(x, y);
    }
}

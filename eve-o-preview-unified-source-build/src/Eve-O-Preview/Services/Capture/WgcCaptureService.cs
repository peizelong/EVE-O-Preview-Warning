using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EveOPreview.Services.Capture
{
    /// <summary>
    /// 基于 PrintWindow API 的 per-window 单帧抓取。
    /// 不依赖 DWM 缩略图，可与 DWM 缩略图并存。
    /// 行为：
    ///   - 窗口最小化 / 不可见 → 返回 false（调用方回退到 DWM）
    ///   - 抓帧失败（PrintWindow 返回 0） → 返回 false
    ///   - 成功 → 返回 true 并填充 32bpp Bitmap（客户区尺寸）
    /// </summary>
    public sealed class WgcCaptureService : IWgcCaptureService
    {
        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        // PW_RENDERFULLCONTENT = 0x00000002
        // 允许 PrintWindow 抓取 DirectComposition / DWM 合成的内容（包括被遮挡部分）。
        private const uint PW_RENDERFULLCONTENT = 0x00000002;

        public bool IsSupported => true;

        public bool TryCaptureFrame(IntPtr hwnd, out Bitmap frame)
        {
            frame = null;
            if (hwnd == IntPtr.Zero) return false;
            if (!IsWindow(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
            if (!GetClientRect(hwnd, out var rc)) return false;

            int width = rc.Right - rc.Left;
            int height = rc.Bottom - rc.Top;
            if (width <= 0 || height <= 0) return false;

            Bitmap bmp;
            try
            {
                bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[WgcCapture] 分配 Bitmap 失败: {ex.Message}");
                return false;
            }

            try
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    var hdc = g.GetHdc();
                    try
                    {
                        if (!PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT))
                        {
                            return false;
                        }
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }
                frame = bmp;
                return true;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[WgcCapture] PrintWindow 失败: {ex.Message}");
                bmp.Dispose();
                return false;
            }
        }
    }
}

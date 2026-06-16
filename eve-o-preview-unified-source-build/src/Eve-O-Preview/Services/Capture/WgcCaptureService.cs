using System;
using EveOPreview.Services.Detection;
using Windows.Graphics.Capture;

namespace EveOPreview.Services.Capture
{
    /// <summary>
    /// 基于 Windows.Graphics.Capture (WGC) 的窗口捕获服务工厂。
    /// 不再做成"给定 HWND 同步截一张图"的同步服务，而是为每个窗口创建独立的
    /// IWindowCaptureSession，由调用方持有 session 生命周期。
    /// 这样多窗口不会互相 Stop/Start session。
    /// </summary>
    public sealed class WgcCaptureService : IWindowCaptureService
    {
        public bool IsSupported
        {
            get
            {
                try { return GraphicsCaptureSession.IsSupported(); }
                catch (Exception ex) { DetectionLog.Write($"[WGC] IsSupported异常: {ex}"); return false; }
            }
        }

        /// <summary>
        /// 为指定窗口创建一个独立的 WGC 捕获会话。
        /// 调用方负责 Start/Stop/Dispose。
        /// </summary>
        public IWindowCaptureSession CreateSession(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                DetectionLog.Write("[WGC] CreateSession 失败: hwnd 为 Zero");
                return null;
            }

            if (!IsSupported)
            {
                DetectionLog.Write("[WGC] CreateSession 失败: 平台不支持 WGC");
                return null;
            }

            return new WgcCaptureSession(hwnd);
        }
    }
}

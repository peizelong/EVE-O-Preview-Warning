using System;

namespace EveOPreview.Services.Capture
{
    /// <summary>
    /// 窗口捕获服务工厂。
    /// WGC 是会话型 API，不应做成"给定 HWND 同步截一张图"。
    /// 正确用法：为每个窗口创建一个独立的 IWindowCaptureSession，
    /// 由调用方持有 session 生命周期，多窗口不会互相 Stop/Start。
    /// </summary>
    public interface IWindowCaptureService
    {
        /// <summary>当前平台是否支持 WGC 抓帧。</summary>
        bool IsSupported { get; }

        /// <summary>
        /// 为指定窗口创建一个独立的捕获会话。
        /// 调用方负责 Start/Stop/Dispose。
        /// </summary>
        IWindowCaptureSession CreateSession(IntPtr hwnd);
    }
}

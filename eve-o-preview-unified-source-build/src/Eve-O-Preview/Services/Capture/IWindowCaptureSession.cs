using System;
using System.Drawing;

namespace EveOPreview.Services.Capture
{
    /// <summary>
    /// 单个窗口的持续捕获会话。
    /// WGC 是会话型 API：Start 后 FrameArrived 持续到达，调用方从最新帧缓存取图。
    /// 每个窗口应持有自己的 session，避免多窗口互相抢 session。
    /// </summary>
    public interface IWindowCaptureSession : IDisposable
    {
        /// <summary>目标窗口句柄。</summary>
        IntPtr WindowHandle { get; }

        /// <summary>会话是否正在运行。</summary>
        bool IsRunning { get; }

        /// <summary>当前实现是否支持抓帧（与平台/窗口能力相关）。</summary>
        bool IsSupported { get; }

        /// <summary>新帧到达时触发（在 WGC 回调线程上触发，订阅者必须自行做线程同步）。</summary>
        event Action<Bitmap> FrameArrived;

        /// <summary>启动持续捕获会话。重复调用安全。</summary>
        void Start();

        /// <summary>停止捕获会话并释放 WGC 资源。重复调用安全。</summary>
        void Stop();

        /// <summary>
        /// 尝试获取自上次调用以来到达的新帧。
        /// 仅当有新帧时返回 true，避免返回旧帧。
        /// 返回的 Bitmap 调用方负责 Dispose。
        /// </summary>
        bool TryGetLatestFrame(out Bitmap frame);
    }
}

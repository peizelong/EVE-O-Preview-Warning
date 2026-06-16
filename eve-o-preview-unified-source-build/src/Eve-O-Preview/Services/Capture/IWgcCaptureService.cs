using System;
using System.Drawing;

namespace EveOPreview.Services.Capture
{
    /// <summary>
    /// 单帧抓取接口。
    /// 当前默认实现走 PrintWindow（兼容 Win7+），未来可替换为 Windows.Graphics.Capture（WGC）。
    /// 接口契约保持一致：给定 HWND → 输出 Bitmap。
    /// </summary>
    public interface IWgcCaptureService
    {
        /// <summary>当前实现是否支持抓帧（与平台/窗口能力相关）。</summary>
        bool IsSupported { get; }

        /// <summary>
        /// 同步抓取目标窗口的当前客户区帧。
        /// 返回 false 时，调用方应继续使用 DWM 缩略图作为回退。
        /// </summary>
        bool TryCaptureFrame(IntPtr hwnd, out Bitmap frame);
    }
}

using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using EveOPreview.Configuration;
using EveOPreview.Mediator.Messages.Detection;
using EveOPreview.Services.Capture;
using MediatR;

namespace EveOPreview.Services.Detection
{
    /// <summary>
    /// 单个 thumbnail 的检测循环。带迟滞状态机：
    ///  - 上升：连续 AlertConfirmationFrames 命中才进入告警态
    ///  - 下降：连续 AlertClearFrames 未命中 且 已过 MinAlertDurationMs 才解除
    ///  - 声光分离：声音只受上升沿/解除控制一次
    /// </summary>
    public sealed class ThumbnailWarningMonitor : IDisposable
    {
        private readonly IThumbnailConfiguration _config;
        private readonly IWgcCaptureService _wgc;
        private readonly ImageTemplateDetector _detector;
        private readonly IMediator _mediator;
        private readonly string _title;
        private readonly IntPtr _hwnd;
        private CancellationTokenSource _cts;
        private bool _isAlerting;
        private int _confirmationCounter;
        private int _clearCounter;
        private DateTime _alertStartedAtUtc;
        private bool _disposed;

        public ThumbnailWarningMonitor(
            IThumbnailConfiguration config,
            IWgcCaptureService wgc,
            ImageTemplateDetector detector,
            IMediator mediator,
            string title,
            IntPtr hwnd)
        {
            _config = config;
            _wgc = wgc;
            _detector = detector;
            _mediator = mediator;
            _title = title;
            _hwnd = hwnd;
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => Loop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
        }

        private async Task Loop(CancellationToken token)
        {
            DetectionLog.Write($"[{_title}] 监控循环启动");
            bool templatesLoaded = _detector.HasTemplatesLoaded();
            DetectionLog.Write($"[{_title}] 模板已加载: {templatesLoaded} (白={_detector.HasTemplate(ColorType.White)}, 红={_detector.HasTemplate(ColorType.Red)}, 橙={_detector.HasTemplate(ColorType.Orange)})");
            DetectionLog.Write($"[{_title}] WGC IsSupported: {_wgc.IsSupported}");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_config.EnableTemplateDetection
                        && _wgc.IsSupported
                        && _wgc.TryCaptureFrame(_hwnd, out var bmp)
                        && bmp != null)
                    {
                        using (bmp)
                        {
                            DetectionLog.Write($"[{_title}] 捕获成功: {bmp.Width}x{bmp.Height}");
                            var roi = _config.GetRoi(_title);
                            var searchRegion = roi == Rectangle.Empty
                                ? new Rectangle(0, 0, bmp.Width, bmp.Height)
                                : Clamp(roi, bmp.Width, bmp.Height);
                            DetectionLog.Write($"[{_title}] 搜索区域: {searchRegion}");

                            _detector.MatchThreshold = _config.TemplateMatchThreshold;
                            DetectionLog.Write($"[{_title}] 开始模板匹配, 阈值={_detector.MatchThreshold:F2}");
                            var result = await _detector.DetectInRegionAsync(bmp, searchRegion);
                            DetectionLog.Write($"[{_title}] 匹配结果: 红={result.RedMatches.Count}, 橙={result.OrangeMatches.Count}, 白={result.WhiteMatches.Count}, 告警={result.HasAlert}");
                            if (result.HasAlert)
                            {
                                foreach (var m in result.AllMatches)
                                    DetectionLog.Write($"[{_title}]   -> 匹配: {m.ColorType} @ ({m.Location.X},{m.Location.Y}) 相似度={m.Similarity:F3} OCR='{m.RecognizedText}'");
                            }
                            UpdateStateMachine(result);

                            // 发布进度事件用于 UI 更新
                            await _mediator.Publish(new TemplateDetectionProgress(_title, result, true)).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        DetectionLog.Write($"[{_title}] 捕获失败或条件不满足: EnableDetect={_config.EnableTemplateDetection}, Supported={_wgc.IsSupported}");
                    }
                }
                catch (Exception ex)
                {
                    DetectionLog.Write($"[{_title}] 循环异常: {ex.Message}");
                    System.Console.WriteLine($"[Monitor:{_title}] {ex.Message}");
                }

                int delay = Math.Max(50, _config.TemplateScanIntervalMs);
                try { await Task.Delay(delay, token); }
                catch (TaskCanceledException) { break; }
            }
        }

        private void UpdateStateMachine(TemplateMatchResult result)
        {
            if (result.HasAlert)
            {
                _confirmationCounter++;
                _clearCounter = 0;

                if (!_isAlerting && _confirmationCounter >= _config.AlertConfirmationFrames)
                {
                    _isAlerting = true;
                    _alertStartedAtUtc = DateTime.UtcNow;
                    System.Console.WriteLine($"[Monitor:{_title}] 上升沿 → 告警 (命中 {result.TotalAlertCount} 处)");
                    _mediator.Publish(new TemplateAlertRaised(_title, result));
                    _mediator.Publish(new ThumbnailAlertStateChanged(_title, true, result));
                }
                else if (_isAlerting)
                {
                    // 持续告警中，不再发新消息避免重复报警；只刷新 result 给后续订阅者参考
                    _mediator.Publish(new ThumbnailAlertStateChanged(_title, true, result));
                }
            }
            else
            {
                _confirmationCounter = 0;

                if (_isAlerting)
                {
                    var elapsedMs = (DateTime.UtcNow - _alertStartedAtUtc).TotalMilliseconds;
                    if (elapsedMs < _config.MinAlertDurationMs)
                    {
                        // 最短告警期内忽略 Clear
                        return;
                    }

                    _clearCounter++;
                    if (_clearCounter >= _config.AlertClearFrames)
                    {
                        _isAlerting = false;
                        _clearCounter = 0;
                        System.Console.WriteLine($"[Monitor:{_title}] 下降沿 → 解除 (持续 {elapsedMs:F0}ms)");
                        _mediator.Publish(new TemplateAlertCleared(_title, result));
                        _mediator.Publish(new ThumbnailAlertStateChanged(_title, false, result));
                    }
                }
            }
        }

        private static Rectangle Clamp(Rectangle r, int w, int h)
        {
            int x = Math.Max(0, Math.Min(r.X, w - 1));
            int y = Math.Max(0, Math.Min(r.Y, h - 1));
            int rw = Math.Max(0, Math.Min(r.Width, w - x));
            int rh = Math.Max(0, Math.Min(r.Height, h - y));
            return new Rectangle(x, y, rw, rh);
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
        }
    }
}

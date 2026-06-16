using System;
using EveOPreview.Services.Detection;
using MediatR;

namespace EveOPreview.Mediator.Messages.Detection
{
    public sealed class TemplateAlertRaised : INotification
    {
        public string ClientTitle { get; }
        public TemplateMatchResult Result { get; }

        public TemplateAlertRaised(string clientTitle, TemplateMatchResult result)
        {
            ClientTitle = clientTitle;
            Result = result;
        }
    }

    public sealed class TemplateAlertCleared : INotification
    {
        public string ClientTitle { get; }
        public TemplateMatchResult Result { get; }

        public TemplateAlertCleared(string clientTitle, TemplateMatchResult result)
        {
            ClientTitle = clientTitle;
            Result = result;
        }
    }

    public sealed class ThumbnailAlertStateChanged : INotification
    {
        public string ClientTitle { get; }
        public bool IsAlerting { get; }
        public TemplateMatchResult Result { get; }

        public ThumbnailAlertStateChanged(string clientTitle, bool isAlerting, TemplateMatchResult result)
        {
            ClientTitle = clientTitle;
            IsAlerting = isAlerting;
            Result = result;
        }
    }

    /// <summary>
    /// 每次模板检测扫描周期完成后发布，用于 UI 更新匹配计数和 OCR 文字。
    /// </summary>
    public sealed class TemplateDetectionProgress : INotification
    {
        public string ClientTitle { get; }
        public TemplateMatchResult Result { get; }
        public bool IsMonitoring { get; }
        public DateTime Timestamp { get; }

        public TemplateDetectionProgress(string clientTitle, TemplateMatchResult result, bool isMonitoring)
        {
            ClientTitle = clientTitle;
            Result = result;
            IsMonitoring = isMonitoring;
            Timestamp = DateTime.Now;
        }
    }
}

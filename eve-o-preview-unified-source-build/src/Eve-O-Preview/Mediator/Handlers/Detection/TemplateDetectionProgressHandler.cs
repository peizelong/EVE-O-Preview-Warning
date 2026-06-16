using System.Threading;
using System.Threading.Tasks;
using EveOPreview.Mediator.Messages.Detection;
using EveOPreview.View;
using MediatR;

namespace EveOPreview.Mediator.Handlers.Detection
{
    /// <summary>
    /// 将每个 client 的检测进度转发到 MainForm（检测标签页 UI）。
    /// </summary>
    public sealed class TemplateDetectionProgressHandler : INotificationHandler<TemplateDetectionProgress>
    {
        private readonly IMainFormView _view;

        public TemplateDetectionProgressHandler(IMainFormView view)
        {
            _view = view;
        }

        public Task Handle(TemplateDetectionProgress notification, CancellationToken cancellationToken)
        {
            _view.UpdateDetectionProgress(notification.ClientTitle, notification.Result);
            return Task.CompletedTask;
        }
    }
}

using System.Threading;
using System.Threading.Tasks;
using EveOPreview.Configuration;
using EveOPreview.Mediator.Messages.Detection;
using EveOPreview.Services.Detection;
using MediatR;

namespace EveOPreview.Mediator.Handlers.Detection
{
    public sealed class TemplateAlertRaisedHandler : INotificationHandler<TemplateAlertRaised>
    {
        private readonly AlarmPlayer _alarm;
        private readonly IThumbnailConfiguration _config;

        public TemplateAlertRaisedHandler(AlarmPlayer alarm, IThumbnailConfiguration config)
        {
            _alarm = alarm;
            _config = config;
        }

        public Task Handle(TemplateAlertRaised notification, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(_config.AlarmSoundPath))
            {
                _alarm.CustomSoundPath = _config.AlarmSoundPath;
            }
            _alarm.PlayAlert();   // 只在"上升沿"被调用一次（由迟滞状态机保证）
            return Task.CompletedTask;
        }
    }

    public sealed class TemplateAlertClearedHandler : INotificationHandler<TemplateAlertCleared>
    {
        private readonly AlarmPlayer _alarm;

        public TemplateAlertClearedHandler(AlarmPlayer alarm)
        {
            _alarm = alarm;
        }

        public Task Handle(TemplateAlertCleared notification, CancellationToken cancellationToken)
        {
            _alarm.StopAlert();   // 解除时也只一次
            return Task.CompletedTask;
        }
    }
}

using System.Threading;
using System.Threading.Tasks;
using EveOPreview.Mediator.Messages.Detection;
using EveOPreview.Services;
using EveOPreview.View;
using MediatR;

namespace EveOPreview.Mediator.Handlers.Detection
{
    public sealed class ThumbnailAlertStateChangedHandler : INotificationHandler<ThumbnailAlertStateChanged>
    {
        private readonly IThumbnailManager _thumbnails;

        public ThumbnailAlertStateChangedHandler(IThumbnailManager thumbnails)
        {
            _thumbnails = thumbnails;
        }

        public Task Handle(ThumbnailAlertStateChanged n, CancellationToken ct)
        {
            var view = _thumbnails.GetClientByTitle(n.ClientTitle);
            view?.SetAlertState(n.IsAlerting);
            return Task.CompletedTask;
        }
    }
}

using System.Drawing;
using EveOPreview.View;

namespace EveOPreview.Services
{
	public interface IThumbnailManager
	{
		void Start();
		void Stop();

		void UpdateCycleGroupIndicator();
		void UpdateThumbnailsSize();
		void UpdateThumbnailFrames();

		IThumbnailView GetClientByTitle(string title);
		IThumbnailView GetClientByPointer(System.IntPtr ptr);
		IThumbnailView GetActiveClient();

		Rectangle? RequestRoiSelection(string clientTitle);

		// 检测控制
		void StartDetection();
		void StopDetection();
		bool IsDetectionRunning { get; }
	}
}
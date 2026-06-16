using System;
using System.Collections.Generic;
using System.Drawing;

namespace EveOPreview.View
{
	/// <summary>
	/// Main view interface
	/// Presenter uses it to access GUI properties
	/// </summary>
	public interface IMainFormView : IView
	{
		bool MinimizeToTray { get; set; }

		double ThumbnailOpacity { get; set; }

		bool EnableClientLayoutTracking { get; set; }
		bool HideActiveClientThumbnail { get; set; }
		bool MinimizeInactiveClients { get; set; }
		bool HideCaptionOnClients { get; set; }
		ViewAnimationStyle WindowsAnimationStyle { get; set; }
        bool ShowThumbnailsAlwaysOnTop { get; set; }
		bool PreventPreviews { get; set; }
		bool HideThumbnailsOnLostFocus { get; set; }
		bool EnablePerClientThumbnailLayouts { get; set; }

		Size ThumbnailSize { get; set; }

		bool EnableThumbnailZoom { get; set; }
		int ThumbnailZoomFactor { get; set; }
		ViewZoomAnchor ThumbnailZoomAnchor { get; set; }
		ViewZoomAnchor OverlayLabelAnchor { get; set; }
		ViewZoomAnchor CycleGroupIndicatorAnchor { get; set; }

		bool ShowThumbnailOverlays { get; set; }
		bool ShowThumbnailFrames { get; set; }

		bool LockThumbnailLocation { get; set; }
		bool ThumbnailSnapToGrid { get; set; }
		int ThumbnailSnapToGridSizeX { get; set; }
		int ThumbnailSnapToGridSizeY { get; set; }

		bool EnableActiveClientHighlight { get; set; }
		Color ActiveClientHighlightColor { get; set; }
		Color PreventPreviewColor { get; set; }
		Color OverlayLabelColor { get; set; }
		Font OverlayLabelFont { get; set; }

		string IconName { get; set; }

		void SetDocumentationUrl(string url);
		void SetVersionInfo(string version);
		void SetThumbnailSizeLimitations(Size minimumSize, Size maximumSize);

		void Minimize();

		void AddThumbnails(IList<IThumbnailDescription> thumbnails);
		void RemoveThumbnails(IList<IThumbnailDescription> thumbnails);
		void RefreshZoomSettings();

		Action ApplicationExitRequested { get; set; }
		Action FormActivated { get; set; }
		Action FormMinimized { get; set; }
		Action<ViewCloseRequest> FormCloseRequested { get; set; }
		Action ApplicationSettingsChanged { get; set; }
		Action ThumbnailsSizeChanged { get; set; }
		Action<string> ThumbnailStateChanged { get; set; }
		Action DocumentationLinkActivated { get; set; }
		Action<string> RoiSelectionRequested { get; set; }
		Action<string> RoiClearRequested { get; set; }
		Action ToggleDetectionRequested { get; set; }
		Action TestSoundRequested { get; set; }

		// ===== 检测设置 =====
		bool EnableTemplateDetection { get; set; }
		double TemplateMatchThreshold { get; set; }
		int TemplateScanIntervalMs { get; set; }
		string AlarmSoundPath { get; set; }
		int AlertConfirmationFrames { get; set; }
		int AlertClearFrames { get; set; }
		int MinAlertDurationMs { get; set; }
		/// <summary>刷新 ROI 客户端列表的内容</summary>
		void RefreshRoiClientList(IList<string> clientTitles);
		/// <summary>更新检测进度（匹配计数、OCR文字等），由 MediatR handler 在后台线程调用</summary>
		void UpdateDetectionProgress(string clientTitle, Services.Detection.TemplateMatchResult result);
		/// <summary>设置检测运行状态（更新按钮文字/颜色）</summary>
		void SetDetectionRunning(bool running);
	}
}
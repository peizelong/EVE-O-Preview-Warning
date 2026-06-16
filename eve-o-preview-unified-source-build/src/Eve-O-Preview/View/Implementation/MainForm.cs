using EveOPreview.Configuration;
using EveOPreview.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace EveOPreview.View
{
	public partial class MainForm : Form, IMainFormView
	{
		#region Private fields
		private readonly ApplicationContext _context;
		private readonly Dictionary<ViewZoomAnchor, RadioButton> _zoomAnchorMap;
		private readonly Dictionary<ViewZoomAnchor, RadioButton> _overlayLabelMap;
		private readonly Dictionary<ViewZoomAnchor, RadioButton> _cycleGroupIndicatorMap;
		private ViewZoomAnchor _cachedThumbnailZoomAnchor;
		private ViewZoomAnchor _cachedOverlayLabelAnchor;
		private ViewZoomAnchor _cachedCycleGroupIndicatorAnchor;
		private bool _suppressEvents;
		private Size _minimumSize;
		private Size _maximumSize;
		private string _iconName;
		#endregion

		public MainForm(ApplicationContext context)
		{
			this._context = context;
			this._zoomAnchorMap = new Dictionary<ViewZoomAnchor, RadioButton>();
			this._overlayLabelMap = new Dictionary<ViewZoomAnchor, RadioButton>();
			this._cycleGroupIndicatorMap = new Dictionary<ViewZoomAnchor, RadioButton>();
			this._cachedThumbnailZoomAnchor = ViewZoomAnchor.NW;
			this._suppressEvents = false;
			this._minimumSize = new Size(20, 20);
			this._maximumSize = new Size(20, 20);

			InitializeComponent();

			this.ThumbnailsList.DisplayMember = "Title";

			this.InitZoomAnchorMap();
			this.InitOverlayLabelMap();
			this.InitCycleGroupIndicatorMap();
			this.InitFormSize();

			this.AnimationStyleCombo.DataSource = Enum.GetValues(typeof(AnimationStyle));

			InitDetectionTab();
		}

		private void InitDetectionTab()
		{
			var tabCtrl = this.Controls.Find("ContentTabControl", true).FirstOrDefault() as TabControl;
			if (tabCtrl == null) return;

			var page = new TabPage("检测");
			var panel = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, AutoScroll = true };
			page.Controls.Add(panel);

			int y = 10;
			const int left = 10;
			const int ctrlW = 120;
			const int labelW = 110;

			// ══════ 监控状态与控制 ══════
			LblDetectionStatus = new Label
			{
				Text = "状态: 已停止", Location = new Point(left, y), Size = new Size(200, 22),
				Font = new Font(Font, FontStyle.Bold), ForeColor = Color.Gray
			};
			panel.Controls.Add(LblDetectionStatus);
			y += 24;

			BtnToggleDetection = new Button { Text = "开始监控", Location = new Point(left, y), Size = new Size(100, 26), BackColor = Color.LightGreen };
			BtnToggleDetection.Click += (s, e) => ToggleDetectionRequested?.Invoke();
			panel.Controls.Add(BtnToggleDetection);

			BtnTestSound = new Button { Text = "测试声音", Location = new Point(left + 110, y), Size = new Size(80, 26) };
			BtnTestSound.Click += (s, e) => TestSoundRequested?.Invoke();
			panel.Controls.Add(BtnTestSound);
			y += 32;

			// 匹配计数
			LblRedCount = new Label { Text = "红名: 0", Location = new Point(left, y), Size = new Size(80, 20), ForeColor = Color.Red };
			LblOrangeCount = new Label { Text = "橙名: 0", Location = new Point(left + 85, y), Size = new Size(80, 20), ForeColor = Color.Orange };
			LblWhiteCount = new Label { Text = "白名: 0", Location = new Point(left + 170, y), Size = new Size(80, 20), ForeColor = Color.Gray };
			panel.Controls.Add(LblRedCount);
			panel.Controls.Add(LblOrangeCount);
			panel.Controls.Add(LblWhiteCount);
			y += 22;

			// OCR 文字
			panel.Controls.Add(new Label { Text = "识别文字:", Location = new Point(left, y), Size = new Size(60, 18) });
			TxtOcrResult = new TextBox
			{
				Location = new Point(left + 60, y), Size = new Size(240, 40),
				Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White
			};
			panel.Controls.Add(TxtOcrResult);
			y += 46;

			// Log 框
			panel.Controls.Add(new Label { Text = "日志:", Location = new Point(left, y), Size = new Size(40, 18) });
			LbxLog = new ListBox
			{
				Location = new Point(left + 40, y), Size = new Size(260, 50), IntegralHeight = false,
				HorizontalScrollbar = true, BackColor = Color.White
			};
			panel.Controls.Add(LbxLog);
			y += 56;

			// ══════ 分隔线 ══════
			panel.Controls.Add(new Label { Text = "──────────────────────────", Location = new Point(left, y), Size = new Size(290, 12) });
			y += 14;

			// ══════ 检测设置 ══════
			CbEnableDetection = new CheckBox { Text = "启用模板检测", Location = new Point(left, y), Size = new Size(200, 20), Checked = true };
			CbEnableDetection.CheckedChanged += (s, e) => OptionChanged_Handler(s, e);
			panel.Controls.Add(CbEnableDetection);
			y += 22;

			panel.Controls.Add(new Label { Text = "匹配阈值", Location = new Point(left, y + 2), Size = new Size(labelW, 18) });
			NumMatchThreshold = new NumericUpDown
			{
				Location = new Point(left + labelW, y), Size = new Size(ctrlW, 22),
				Minimum = 0, Maximum = 100, DecimalPlaces = 1, Increment = 5, Value = 85
			};
			NumMatchThreshold.ValueChanged += OptionChanged_Handler;
			panel.Controls.Add(NumMatchThreshold);
			y += 24;

			panel.Controls.Add(new Label { Text = "扫描间隔(ms)", Location = new Point(left, y + 2), Size = new Size(labelW, 18) });
			NumScanInterval = new NumericUpDown
			{
				Location = new Point(left + labelW, y), Size = new Size(ctrlW, 22),
				Minimum = 100, Maximum = 5000, Increment = 100, Value = 500
			};
			NumScanInterval.ValueChanged += OptionChanged_Handler;
			panel.Controls.Add(NumScanInterval);
			y += 24;

			panel.Controls.Add(new Label { Text = "报警声音路径", Location = new Point(left, y + 2), Size = new Size(labelW, 18) });
			TxtAlarmPath = new TextBox { Location = new Point(left + labelW, y), Size = new Size(120, 22), ReadOnly = true };
			BtnBrowseAlarm = new Button { Text = "浏览", Location = new Point(left + labelW + 125, y), Size = new Size(50, 22) };
			BtnBrowseAlarm.Click += (s, e) =>
			{
				using var dlg = new OpenFileDialog { Filter = "音频文件|*.wav;*.mp3", Title = "选择报警声音文件" };
				if (dlg.ShowDialog() == DialogResult.OK)
				{
					TxtAlarmPath.Text = dlg.FileName;
					OptionChanged_Handler(s, e);
				}
			};
			panel.Controls.Add(TxtAlarmPath);
			panel.Controls.Add(BtnBrowseAlarm);
			y += 24;

			panel.Controls.Add(new Label { Text = "告警确认帧数", Location = new Point(left, y + 2), Size = new Size(labelW, 18) });
			NumConfirmFrames = new NumericUpDown
			{
				Location = new Point(left + labelW, y), Size = new Size(ctrlW, 22),
				Minimum = 1, Maximum = 20, Increment = 1, Value = 2
			};
			NumConfirmFrames.ValueChanged += OptionChanged_Handler;
			panel.Controls.Add(NumConfirmFrames);
			y += 24;

			panel.Controls.Add(new Label { Text = "告警清除帧数", Location = new Point(left, y + 2), Size = new Size(labelW, 18) });
			NumClearFrames = new NumericUpDown
			{
				Location = new Point(left + labelW, y), Size = new Size(ctrlW, 22),
				Minimum = 1, Maximum = 30, Increment = 1, Value = 4
			};
			NumClearFrames.ValueChanged += OptionChanged_Handler;
			panel.Controls.Add(NumClearFrames);
			y += 24;

			panel.Controls.Add(new Label { Text = "最短告警时长(ms)", Location = new Point(left, y + 2), Size = new Size(labelW, 18) });
			NumMinAlertDuration = new NumericUpDown
			{
				Location = new Point(left + labelW, y), Size = new Size(ctrlW, 22),
				Minimum = 0, Maximum = 30000, Increment = 200, Value = 2000
			};
			NumMinAlertDuration.ValueChanged += OptionChanged_Handler;
			panel.Controls.Add(NumMinAlertDuration);
			y += 28;

			// ══════ ROI 管理 ══════
			panel.Controls.Add(new Label { Text = "────────── ROI 管理 ──────────", Location = new Point(left, y), Size = new Size(290, 12) });
			y += 14;

			RoiClientList = new ListBox { Location = new Point(left, y), Size = new Size(190, 60), IntegralHeight = false };
			RoiClientList.SelectedIndexChanged += (s, e) =>
			{
				bool sel = RoiClientList.SelectedItem != null;
				BtnSetRoi.Enabled = sel; BtnClearRoi.Enabled = sel;
			};
			panel.Controls.Add(RoiClientList);

			BtnSetRoi = new Button { Text = "设置 ROI", Location = new Point(left + 200, y), Size = new Size(85, 24), Enabled = false };
			BtnSetRoi.Click += (s, e) =>
			{
				if (RoiClientList.SelectedItem is string title) RoiSelectionRequested?.Invoke(title);
			};
			panel.Controls.Add(BtnSetRoi);

			BtnClearRoi = new Button { Text = "清除 ROI", Location = new Point(left + 200, y + 28), Size = new Size(85, 24), Enabled = false };
			BtnClearRoi.Click += (s, e) =>
			{
				if (RoiClientList.SelectedItem is string title) RoiClearRequested?.Invoke(title);
			};
			panel.Controls.Add(BtnClearRoi);

			tabCtrl.Controls.Add(page);
			DetectionTabPage = page;
		}

		public bool MinimizeToTray
		{
			get => this.MinimizeToTrayCheckBox.Checked;
			set => this.MinimizeToTrayCheckBox.Checked = value;
		}

		public string IconName
		{
			get => this._iconName;
			set
			{


				this._iconName = value;

				// Set Icon 
				System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
				if (this._iconName == null || ((resources.GetObject(this._iconName))) == null)
				{
					this._iconName = "IconOriginal";
				}

				// pull icon from resources
				try
				{
					var iconBytes = (byte[])resources.GetObject(this._iconName);
					using (MemoryStream ms = new MemoryStream(iconBytes))
					{
						this.Icon = new Icon(ms);
						this.NotifyIcon.Icon = this.Icon;
					}
				}
				catch (Exception ex)
				{
					// Log ?
				}

				if (value != "")
				{
					this.ApplicationSettingsChanged?.Invoke();
				}
			}
		}

		public double ThumbnailOpacity
		{
			get => Math.Min(this.ThumbnailOpacityTrackBar.Value / 100.00, 1.00);
			set
			{
				int barValue = (int)(100.0 * value);
				if (barValue > 100)
				{
					barValue = 100;
				}
				else if (barValue < 10)
				{
					barValue = 10;
				}

				this.ThumbnailOpacityTrackBar.Value = barValue;
			}
		}

		public bool EnableClientLayoutTracking
		{
			get => this.EnableClientLayoutTrackingCheckBox.Checked;
			set => this.EnableClientLayoutTrackingCheckBox.Checked = value;
		}

		public bool HideActiveClientThumbnail
		{
			get => this.HideActiveClientThumbnailCheckBox.Checked;
			set => this.HideActiveClientThumbnailCheckBox.Checked = value;
		}

		public bool MinimizeInactiveClients
		{
			get => this.MinimizeInactiveClientsCheckBox.Checked;
			set => this.MinimizeInactiveClientsCheckBox.Checked = value;
		}
		public bool HideCaptionOnClients
		{
			get => this.HideCaptionOnClientsCheckBox.Checked;
			set => this.HideCaptionOnClientsCheckBox.Checked = value;
		}
		public ViewAnimationStyle WindowsAnimationStyle
		{
			get => (ViewAnimationStyle)this.AnimationStyleCombo.SelectedItem;
			set => this.AnimationStyleCombo.SelectedIndex = (int)value;
		}

		public bool ShowThumbnailsAlwaysOnTop
		{
			get => this.ShowThumbnailsAlwaysOnTopCheckBox.Checked;
			set => this.ShowThumbnailsAlwaysOnTopCheckBox.Checked = value;
		}
		public bool PreventPreviews
		{
			get => this.PreventPreviewsCheckBox.Checked;
			set => this.PreventPreviewsCheckBox.Checked = value;
		}

		public bool HideThumbnailsOnLostFocus
		{
			get => this.HideThumbnailsOnLostFocusCheckBox.Checked;
			set => this.HideThumbnailsOnLostFocusCheckBox.Checked = value;
		}

		public bool EnablePerClientThumbnailLayouts
		{
			get => this.EnablePerClientThumbnailsLayoutsCheckBox.Checked;
			set => this.EnablePerClientThumbnailsLayoutsCheckBox.Checked = value;
		}

		public Size ThumbnailSize
		{
			get => new Size((int)this.ThumbnailsWidthNumericEdit.Value, (int)this.ThumbnailsHeightNumericEdit.Value);
			set
			{
				this.ThumbnailsWidthNumericEdit.Value = value.Width;
				this.ThumbnailsHeightNumericEdit.Value = value.Height;
			}
		}

		public bool EnableThumbnailZoom
		{
			get => this.EnableThumbnailZoomCheckBox.Checked;
			set
			{
				this.EnableThumbnailZoomCheckBox.Checked = value;
				this.RefreshZoomSettings();
			}
		}

		public int ThumbnailZoomFactor
		{
			get => (int)this.ThumbnailZoomFactorNumericEdit.Value;
			set => this.ThumbnailZoomFactorNumericEdit.Value = value;
		}

		public ViewZoomAnchor ThumbnailZoomAnchor
		{
			get
			{
				if (this._zoomAnchorMap[this._cachedThumbnailZoomAnchor].Checked)
				{
					return this._cachedThumbnailZoomAnchor;
				}

				foreach (KeyValuePair<ViewZoomAnchor, RadioButton> valuePair in this._zoomAnchorMap)
				{
					if (!valuePair.Value.Checked)
					{
						continue;
					}

					this._cachedThumbnailZoomAnchor = valuePair.Key;
					return this._cachedThumbnailZoomAnchor;
				}

				// Default value
				return ViewZoomAnchor.NW;
			}
			set
			{
				this._cachedThumbnailZoomAnchor = value;
				this._zoomAnchorMap[this._cachedThumbnailZoomAnchor].Checked = true;
			}
		}

		public ViewZoomAnchor OverlayLabelAnchor
		{
			get
			{
				if (this._overlayLabelMap[this._cachedOverlayLabelAnchor].Checked)
				{
					return this._cachedOverlayLabelAnchor;
				}

				foreach (KeyValuePair<ViewZoomAnchor, RadioButton> valuePair in this._overlayLabelMap)
				{
					if (!valuePair.Value.Checked)
					{
						continue;
					}

					this._cachedOverlayLabelAnchor = valuePair.Key;
					return this._cachedOverlayLabelAnchor;
				}

				// Default Value
				return ViewZoomAnchor.NW;
			}
			set
			{
				this._cachedOverlayLabelAnchor = value;
				this._overlayLabelMap[this._cachedOverlayLabelAnchor].Checked = true;
			}
		}

		public ViewZoomAnchor CycleGroupIndicatorAnchor
		{
			get
			{
				if (this._cycleGroupIndicatorMap[this._cachedCycleGroupIndicatorAnchor].Checked)
				{
					return this._cachedCycleGroupIndicatorAnchor;
				}

				foreach (KeyValuePair<ViewZoomAnchor, RadioButton> valuePair in this._cycleGroupIndicatorMap)
				{
					if (!valuePair.Value.Checked)
					{
						continue;
					}

					this._cachedCycleGroupIndicatorAnchor = valuePair.Key;
					return this._cachedCycleGroupIndicatorAnchor;
				}

				// Default Value
				return ViewZoomAnchor.NW;
			}
			set
			{
				this._cachedCycleGroupIndicatorAnchor = value;
				this._cycleGroupIndicatorMap[this._cachedCycleGroupIndicatorAnchor].Checked = true;
			}
		}

		public bool ShowThumbnailOverlays
		{
			get => this.ShowThumbnailOverlaysCheckBox.Checked;
			set => this.ShowThumbnailOverlaysCheckBox.Checked = value;
		}

		public bool ShowThumbnailFrames
		{
			get => this.ShowThumbnailFramesCheckBox.Checked;
			set => this.ShowThumbnailFramesCheckBox.Checked = value;
		}
		public bool LockThumbnailLocation
		{
			get => this.LockThumbnailLocationCheckbox.Checked;
			set => this.LockThumbnailLocationCheckbox.Checked = value;
		}
		public bool ThumbnailSnapToGrid
		{
			get => this.ThumbnailSnapToGridCheckBox.Checked;
			set => this.ThumbnailSnapToGridCheckBox.Checked = value;
		}
		public int ThumbnailSnapToGridSizeX
		{
			get => (int)ThumbnailSnapToGridSizeXNumericEdit.Value;
			set => ThumbnailSnapToGridSizeXNumericEdit.Value = value;
		}
		public int ThumbnailSnapToGridSizeY
		{
			get => (int)ThumbnailSnapToGridSizeYNumericEdit.Value;
			set => ThumbnailSnapToGridSizeYNumericEdit.Value = value;
		}

		public bool EnableActiveClientHighlight
		{
			get => this.EnableActiveClientHighlightCheckBox.Checked;
			set => this.EnableActiveClientHighlightCheckBox.Checked = value;
		}

		public Color ActiveClientHighlightColor
		{
			get => this._activeClientHighlightColor;
			set
			{
				this._activeClientHighlightColor = value;
				this.ActiveClientHighlightColorButton.BackColor = value;
			}
		}
		private Color _activeClientHighlightColor;

		public Color PreventPreviewColor
		{
			get => this._preventPreviewColor;
			set
			{
				this._preventPreviewColor = value;
				this.PreventPreviewColorButton.BackColor = value;
			}
		}
		private Color _preventPreviewColor;

		public Color OverlayLabelColor
		{
			get => this._OverlayLabelColor;
			set
			{
				this._OverlayLabelColor = value;
				this.OverlayLabelColorButton.BackColor = value;
			}
		}
		private Color _OverlayLabelColor;

		public Font OverlayLabelFont
		{
			get => (Font)this._OverlayLabelFont;
			set
			{
				this._OverlayLabelFont = value;
				this.LabelOverlayLabelFont.Font = value;
			}
		}
		private Font _OverlayLabelFont;

		public new void Show()
		{
			// Registers the current instance as the application's Main Form
			this._context.MainForm = this;

			this._suppressEvents = true;
			this.FormActivated?.Invoke();
			this._suppressEvents = false;

			Application.Run(this._context);
		}

		public void SetThumbnailSizeLimitations(Size minimumSize, Size maximumSize)
		{
			this._minimumSize = minimumSize;
			this._maximumSize = maximumSize;
		}

		public void Minimize()
		{
			this.WindowState = FormWindowState.Minimized;
		}

		public void SetVersionInfo(string version)
		{
			this.VersionLabel.Text = version;
		}

		public void SetDocumentationUrl(string url)
		{
			this.DocumentationLink.Text = url;
		}

		public void AddThumbnails(IList<IThumbnailDescription> thumbnails)
		{
			this.ThumbnailsList.BeginUpdate();

			foreach (IThumbnailDescription view in thumbnails)
			{
				this.ThumbnailsList.SetItemChecked(this.ThumbnailsList.Items.Add(view), view.IsDisabled);
			}

			this.ThumbnailsList.EndUpdate();
		}

		public void RemoveThumbnails(IList<IThumbnailDescription> thumbnails)
		{
			this.ThumbnailsList.BeginUpdate();

			foreach (IThumbnailDescription view in thumbnails)
			{
				this.ThumbnailsList.Items.Remove(view);
			}

			this.ThumbnailsList.EndUpdate();
		}

		public void RefreshZoomSettings()
		{
			bool enableControls = this.EnableThumbnailZoom;
			this.ThumbnailZoomFactorNumericEdit.Enabled = enableControls;
			this.ZoomAnchorPanel.Enabled = enableControls;
		}

		public Action ApplicationExitRequested { get; set; }

		public Action FormActivated { get; set; }

		public Action FormMinimized { get; set; }

		public Action<ViewCloseRequest> FormCloseRequested { get; set; }

		public Action ApplicationSettingsChanged { get; set; }

		public Action ThumbnailsSizeChanged { get; set; }

		public Action<string> ThumbnailStateChanged { get; set; }

		public Action DocumentationLinkActivated { get; set; }

		public Action<string> RoiSelectionRequested { get; set; }
		public Action<string> RoiClearRequested { get; set; }
		public Action ToggleDetectionRequested { get; set; }
		public Action TestSoundRequested { get; set; }

		// ===== 检测属性实现 =====
		public bool EnableTemplateDetection
		{
			get => CbEnableDetection.Checked;
			set => CbEnableDetection.Checked = value;
		}
		public double TemplateMatchThreshold
		{
			get => (double)NumMatchThreshold.Value / 100.0;
			set => NumMatchThreshold.Value = (decimal)(value * 100.0);
		}
		public int TemplateScanIntervalMs
		{
			get => (int)NumScanInterval.Value;
			set => NumScanInterval.Value = value;
		}
		public string AlarmSoundPath
		{
			get => TxtAlarmPath.Text;
			set => TxtAlarmPath.Text = value;
		}
		public int AlertConfirmationFrames
		{
			get => (int)NumConfirmFrames.Value;
			set => NumConfirmFrames.Value = value;
		}
		public int AlertClearFrames
		{
			get => (int)NumClearFrames.Value;
			set => NumClearFrames.Value = value;
		}
		public int MinAlertDurationMs
		{
			get => (int)NumMinAlertDuration.Value;
			set => NumMinAlertDuration.Value = value;
		}
		public void RefreshRoiClientList(IList<string> clientTitles)
		{
			string prev = RoiClientList.SelectedItem as string;
			RoiClientList.Items.Clear();
			foreach (var t in clientTitles) RoiClientList.Items.Add(t);
			if (prev != null && RoiClientList.Items.Contains(prev))
				RoiClientList.SelectedItem = prev;
		}

		/// <summary>
		/// 由 MediatR handler 从后台线程调用，用 BeginInvoke 封送到 UI 线程。
		/// </summary>
		public void UpdateDetectionProgress(string clientTitle, Services.Detection.TemplateMatchResult result)
		{
			if (this.IsDisposed) return;
			try
			{
				if (this.InvokeRequired)
				{
					this.BeginInvoke(new Action(() => UpdateDetectionProgressUi(clientTitle, result)));
					return;
				}
				UpdateDetectionProgressUi(clientTitle, result);
			}
			catch { /* 忽略已释放窗体的异常 */ }
		}

		public void SetDetectionRunning(bool running)
		{
			if (this.IsDisposed) return;
			try
			{
				if (this.InvokeRequired)
				{
					this.BeginInvoke(new Action(() => SetDetectionRunningUi(running)));
					return;
				}
				SetDetectionRunningUi(running);
			}
			catch { }
		}

		private void SetDetectionRunningUi(bool running)
		{
			if (running)
			{
				LblDetectionStatus.Text = "状态: 监控中";
				LblDetectionStatus.ForeColor = Color.Green;
				BtnToggleDetection.Text = "停止监控";
				BtnToggleDetection.BackColor = Color.IndianRed;
			}
			else
			{
				LblDetectionStatus.Text = "状态: 已停止";
				LblDetectionStatus.ForeColor = Color.Gray;
				BtnToggleDetection.Text = "开始监控";
				BtnToggleDetection.BackColor = Color.LightGreen;
				LblRedCount.Text = "红名: 0";
				LblOrangeCount.Text = "橙名: 0";
				LblWhiteCount.Text = "白名: 0";
				TxtOcrResult.Clear();
			}
		}

		private void UpdateDetectionProgressUi(string clientTitle, Services.Detection.TemplateMatchResult result)
		{
			// 更新匹配计数（累加该 client 的数值）
			int red = result.RedMatches.Count;
			int orange = result.OrangeMatches.Count;
			int white = result.WhiteMatches.Count;
			int total = red + orange + white;

			LblRedCount.Text = $"红名: {red}";
			LblOrangeCount.Text = $"橙名: {orange}";
			LblWhiteCount.Text = $"白名: {white}";
			if (red > 0) LblRedCount.Font = new Font(LblRedCount.Font, FontStyle.Bold);
			else LblRedCount.Font = new Font(LblRedCount.Font, FontStyle.Regular);
			if (orange > 0) LblOrangeCount.Font = new Font(LblOrangeCount.Font, FontStyle.Bold);
			else LblOrangeCount.Font = new Font(LblOrangeCount.Font, FontStyle.Regular);
			if (white > 0) LblWhiteCount.Font = new Font(LblWhiteCount.Font, FontStyle.Bold);
			else LblWhiteCount.Font = new Font(LblWhiteCount.Font, FontStyle.Regular);
			if (white > 0) LblWhiteCount.ForeColor = Color.Red; else LblWhiteCount.ForeColor = Color.Gray;

			// OCR 文字：收集所有匹配的识别文字
			if (total > 0)
			{
				var texts = new System.Text.StringBuilder();
				foreach (var m in result.RedMatches) AppendMatchText(texts, "红", m);
				foreach (var m in result.OrangeMatches) AppendMatchText(texts, "橙", m);
				foreach (var m in result.WhiteMatches) AppendMatchText(texts, "白", m);
				string ocr = texts.ToString().TrimEnd('\r', '\n');
				if (!string.IsNullOrEmpty(ocr))
				{
					TxtOcrResult.Text = ocr;
				}
			}

			// 状态
			bool hasAlert = result.HasAlert;
			if (hasAlert && LblDetectionStatus.Text != "状态: 警报中!")
			{
				LblDetectionStatus.Text = "状态: 警报中!";
				LblDetectionStatus.ForeColor = Color.Red;
				BtnToggleDetection.BackColor = Color.IndianRed;
				BtnToggleDetection.Text = "停止监控";
			}
		}

		private static void AppendMatchText(System.Text.StringBuilder sb, string prefix, Services.Detection.TemplateMatch match)
		{
			string text = match.RecognizedText;
			if (!string.IsNullOrWhiteSpace(text))
			{
				sb.Append($"[{prefix}] {text}\r\n");
			}
		}

		#region UI events
		private void ContentTabControl_DrawItem(object sender, DrawItemEventArgs e)
		{
			TabControl control = (TabControl)sender;
			TabPage page = control.TabPages[e.Index];
			Rectangle bounds = control.GetTabRect(e.Index);

			Graphics graphics = e.Graphics;

			Brush textBrush = new SolidBrush(SystemColors.ActiveCaptionText);
			Brush backgroundBrush = (e.State == DrawItemState.Selected)
										? new SolidBrush(SystemColors.Control)
										: new SolidBrush(SystemColors.ControlDark);
			graphics.FillRectangle(backgroundBrush, e.Bounds);

			// Use our own font
			Font font = new Font("Arial", this.Font.Size * 1.5f, FontStyle.Bold, GraphicsUnit.Pixel);

			// Draw string and center the text
			StringFormat stringFlags = new StringFormat();
			stringFlags.Alignment = StringAlignment.Center;
			stringFlags.LineAlignment = StringAlignment.Center;

			graphics.DrawString(page.Text, font, textBrush, bounds, stringFlags);
		}

		private void OptionChanged_Handler(object sender, EventArgs e)
		{
			if (this._suppressEvents)
			{
				return;
			}

			this.ApplicationSettingsChanged?.Invoke();
		}

		private void ThumbnailSizeChanged_Handler(object sender, EventArgs e)
		{
			if (this._suppressEvents)
			{
				return;
			}

			// Perform some View work that is not properly done in the Control
			this._suppressEvents = true;
			Size thumbnailSize = this.ThumbnailSize;
			thumbnailSize.Width = Math.Min(Math.Max(thumbnailSize.Width, this._minimumSize.Width), this._maximumSize.Width);
			thumbnailSize.Height = Math.Min(Math.Max(thumbnailSize.Height, this._minimumSize.Height), this._maximumSize.Height);
			this.ThumbnailSize = thumbnailSize;
			this._suppressEvents = false;

			this.ThumbnailsSizeChanged?.Invoke();
		}

		private void ActiveClientHighlightColorButton_Click(object sender, EventArgs e)
		{
			using (ColorDialog dialog = new ColorDialog())
			{
				dialog.Color = this.ActiveClientHighlightColor;

				if (dialog.ShowDialog() != DialogResult.OK)
				{
					return;
				}

				this.ActiveClientHighlightColor = dialog.Color;
			}

			this.OptionChanged_Handler(sender, e);
		}

		private void OverlayLabelColorButton_Click(object sender, EventArgs e)
		{
			using (ColorDialog dialog = new ColorDialog())
			{
				dialog.Color = this.OverlayLabelColor;

				if (dialog.ShowDialog() != DialogResult.OK)
				{
					return;
				}
				this.OverlayLabelColor = dialog.Color;
			}

			this.OptionChanged_Handler(sender, e);
		}

		private void ThumbnailsList_ItemCheck_Handler(object sender, ItemCheckEventArgs e)
		{
			if (!(this.ThumbnailsList.Items[e.Index] is IThumbnailDescription selectedItem))
			{
				return;
			}

			selectedItem.IsDisabled = (e.NewValue == CheckState.Checked);

			this.ThumbnailStateChanged?.Invoke(selectedItem.Title);
		}

		private void DocumentationLinkClicked_Handler(object sender, LinkLabelLinkClickedEventArgs e)
		{
			this.DocumentationLinkActivated?.Invoke();
		}

		private void MainFormResize_Handler(object sender, EventArgs e)
		{
			if (this.WindowState != FormWindowState.Minimized)
			{
				return;
			}

			this.FormMinimized?.Invoke();
		}

		private void MainFormClosing_Handler(object sender, FormClosingEventArgs e)
		{
			ViewCloseRequest request = new ViewCloseRequest();

			this.FormCloseRequested?.Invoke(request);

			e.Cancel = !request.Allow;
		}

		private void RestoreMainForm_Handler(object sender, EventArgs e)
		{
			// This is form's GUI lifecycle event that is invariant to the Form data
			base.Show();
			this.WindowState = FormWindowState.Normal;
			this.BringToFront();
		}

		private void ExitMenuItemClick_Handler(object sender, EventArgs e)
		{
			this.ApplicationExitRequested?.Invoke();
		}
		#endregion

		private void InitZoomAnchorMap()
		{
			this._zoomAnchorMap[ViewZoomAnchor.NW] = this.ZoomAanchorNWRadioButton;
			this._zoomAnchorMap[ViewZoomAnchor.N] = this.ZoomAanchorNRadioButton;
			this._zoomAnchorMap[ViewZoomAnchor.NE] = this.ZoomAanchorNERadioButton;
			this._zoomAnchorMap[ViewZoomAnchor.W] = this.ZoomAanchorWRadioButton;
			this._zoomAnchorMap[ViewZoomAnchor.C] = this.ZoomAanchorCRadioButton;
			this._zoomAnchorMap[ViewZoomAnchor.E] = this.ZoomAanchorERadioButton;
			this._zoomAnchorMap[ViewZoomAnchor.SW] = this.ZoomAanchorSWRadioButton;
			this._zoomAnchorMap[ViewZoomAnchor.S] = this.ZoomAanchorSRadioButton;
			this._zoomAnchorMap[ViewZoomAnchor.SE] = this.ZoomAanchorSERadioButton;
		}
		private void InitOverlayLabelMap()
		{
			this._overlayLabelMap[ViewZoomAnchor.NW] = this.OverlayLabelNWRadioButton;
			this._overlayLabelMap[ViewZoomAnchor.N] = this.OverlayLabelNRadioButton;
			this._overlayLabelMap[ViewZoomAnchor.NE] = this.OverlayLabelNERadioButton;
			this._overlayLabelMap[ViewZoomAnchor.W] = this.OverlayLabelWRadioButton;
			this._overlayLabelMap[ViewZoomAnchor.C] = this.OverlayLabelCRadioButton;
			this._overlayLabelMap[ViewZoomAnchor.E] = this.OverlayLabelERadioButton;
			this._overlayLabelMap[ViewZoomAnchor.SW] = this.OverlayLabelSWRadioButton;
			this._overlayLabelMap[ViewZoomAnchor.S] = this.OverlayLabelSRadioButton;
			this._overlayLabelMap[ViewZoomAnchor.SE] = this.OverlayLabelSERadioButton;
		}
		private void InitCycleGroupIndicatorMap()
		{
			this._cycleGroupIndicatorMap[ViewZoomAnchor.NW] = this.CycleGroupIndicatorNWRadioButton;
			this._cycleGroupIndicatorMap[ViewZoomAnchor.N] = this.CycleGroupIndicatorNRadioButton;
			this._cycleGroupIndicatorMap[ViewZoomAnchor.NE] = this.CycleGroupIndicatorNERadioButton;
			this._cycleGroupIndicatorMap[ViewZoomAnchor.W] = this.CycleGroupIndicatorWRadioButton;
			this._cycleGroupIndicatorMap[ViewZoomAnchor.C] = this.CycleGroupIndicatorCRadioButton;
			this._cycleGroupIndicatorMap[ViewZoomAnchor.E] = this.CycleGroupIndicatorERadioButton;
			this._cycleGroupIndicatorMap[ViewZoomAnchor.SW] = this.CycleGroupIndicatorSWRadioButton;
			this._cycleGroupIndicatorMap[ViewZoomAnchor.S] = this.CycleGroupIndicatorSRadioButton;
			this._cycleGroupIndicatorMap[ViewZoomAnchor.SE] = this.CycleGroupIndicatorSERadioButton;
		}

		private void InitFormSize()
		{
			const int BUFFER_PIXEL_AMOUNT = 8;
			// resize form height based on tabbed control item height
			var tabControl = (System.Windows.Forms.TabControl)this.Controls.Find("ContentTabControl", false).First();
			if (tabControl != null)
			{
				var furnitureSize = this.Height - tabControl.Height;
				var calculatedHeight = (tabControl.ItemSize.Width * tabControl.Controls.Count) + furnitureSize + BUFFER_PIXEL_AMOUNT;
				if (this.Height < calculatedHeight)
				{
					this.Height = calculatedHeight;
				}
			}
		}

		private void btnLabelFont_Click(object sender, EventArgs e)
		{
			FontDialog fontSelector = new FontDialog();
			fontSelector.Font = OverlayLabelFont;
			fontSelector.ShowColor = false;
			fontSelector.ShowApply = false;
			fontSelector.ShowHelp = false;
			if (fontSelector.ShowDialog() != DialogResult.Cancel)
			{
				OverlayLabelFont = fontSelector.Font;
				LabelOverlayLabelFont.Font = fontSelector.Font;
				this.OptionChanged_Handler(sender, e);
			}
		}

		private void PreventPreviewColorButton_Click(object sender, EventArgs e)
		{
			using (ColorDialog dialog = new ColorDialog())
			{
				dialog.Color = this.PreventPreviewColor;

				if (dialog.ShowDialog() != DialogResult.OK)
				{
					return;
				}

				this.PreventPreviewColor = dialog.Color;
			}

			this.OptionChanged_Handler(sender, e);

		}
	}
}
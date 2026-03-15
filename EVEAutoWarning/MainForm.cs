using System.Drawing;
using EVEAutoWarning.Core;

namespace EVEAutoWarning;

public partial class MainForm : Form
{
    private readonly WarningMonitor _monitor;
    private NotifyIcon _trayIcon;
    private ContextMenuStrip _trayMenu;
    private Point _selectionStart;
    private Rectangle _selectedRegion;
    private Form _overlayForm;

    private Label _lblStatus;
    private Button _btnStartStop;
    private Button _btnSelectRegion;
    private Button _btnTestSound;
    private Button _btnSettings;
    private GroupBox _grpRegion;
    private Label _lblRegionInfo;
    private GroupBox _grpStatus;
    private GroupBox _grpTemplate;
    private Label _lblTemplateStatus;
    private Label _lblRedMatch;
    private Label _lblOrangeMatch;
    private Label _lblWhiteMatch;
    private GroupBox _grpLog;
    private ListBox _lstLog;

    public MainForm()
    {
        _monitor = new WarningMonitor();
        InitializeComponent();
        SetupTrayIcon();
        SetupEventHandlers();
        UpdateRegionDisplay();
        CheckTemplatesLoaded();
    }

    private void CheckTemplatesLoaded()
    {
        if (_monitor.TemplateDetector.HasTemplatesLoaded())
        {
            _lblTemplateStatus.Text = "模板状态: 已嵌入加载 (白, 红, 橙)";
            _lblTemplateStatus.ForeColor = Color.Green;
            LogMessage("[信息] 已从程序内部加载模板图片");
        }
        else
        {
            _lblTemplateStatus.Text = "模板状态: 未加载";
            _lblTemplateStatus.ForeColor = Color.Red;
            LogMessage("[警告] 未能加载模板图片");
        }
    }

    private void InitializeComponent()
    {
        this.Text = "EVE 自动预警";
        this.Size = new Size(500, 580);
        this.MinimumSize = new Size(480, 520);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;

        int yPos = 10;

        _grpStatus = new GroupBox
        {
            Text = "监控状态",
            Location = new Point(10, yPos),
            Size = new Size(460, 60)
        };

        _lblStatus = new Label
        {
            Text = "状态: 已停止",
            Location = new Point(10, 20),
            Size = new Size(440, 30),
            Font = new Font("Microsoft YaHei", 12, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };

        _grpStatus.Controls.Add(_lblStatus);

        yPos += 70;

        _grpTemplate = new GroupBox
        {
            Text = "模板检测",
            Location = new Point(10, yPos),
            Size = new Size(460, 100)
        };

        _lblTemplateStatus = new Label
        {
            Text = "模板状态: 未加载",
            Location = new Point(10, 20),
            Size = new Size(440, 20),
            ForeColor = Color.Gray
        };

        _lblRedMatch = new Label
        {
            Text = "🔴 红名: 0",
            Location = new Point(10, 50),
            Size = new Size(140, 25),
            ForeColor = Color.Red,
            Font = new Font("Microsoft YaHei", 10)
        };

        _lblOrangeMatch = new Label
        {
            Text = "🟠 橙名: 0",
            Location = new Point(160, 50),
            Size = new Size(140, 25),
            ForeColor = Color.Orange,
            Font = new Font("Microsoft YaHei", 10)
        };

        _lblWhiteMatch = new Label
        {
            Text = "⚪ 白名: 0",
            Location = new Point(310, 50),
            Size = new Size(140, 25),
            ForeColor = Color.Gray,
            Font = new Font("Microsoft YaHei", 10)
        };

        var lblDetectionMode = new Label
        {
            Text = "检测模式: 模板匹配",
            Location = new Point(10, 75),
            Size = new Size(440, 18),
            ForeColor = Color.Blue
        };

        _grpTemplate.Controls.AddRange(new Control[] { _lblTemplateStatus, _lblRedMatch, _lblOrangeMatch, _lblWhiteMatch, lblDetectionMode });

        yPos += 110;

        _grpRegion = new GroupBox
        {
            Text = "扫描区域",
            Location = new Point(10, yPos),
            Size = new Size(460, 70)
        };

        _lblRegionInfo = new Label
        {
            Text = "区域: 未设置",
            Location = new Point(10, 20),
            Size = new Size(440, 40),
            Font = new Font("Microsoft YaHei", 9)
        };

        _grpRegion.Controls.Add(_lblRegionInfo);

        yPos += 80;

        _btnStartStop = new Button
        {
            Text = "开始监控",
            Location = new Point(10, yPos),
            Size = new Size(145, 45),
            BackColor = Color.LightGreen,
            Font = new Font("Microsoft YaHei", 11, FontStyle.Bold)
        };

        _btnSelectRegion = new Button
        {
            Text = "选择区域",
            Location = new Point(165, yPos),
            Size = new Size(145, 45),
            Font = new Font("Microsoft YaHei", 10)
        };

        _btnTestSound = new Button
        {
            Text = "测试声音",
            Location = new Point(320, yPos),
            Size = new Size(145, 45),
            Font = new Font("Microsoft YaHei", 10)
        };

        yPos += 55;

        _btnSettings = new Button
        {
            Text = "高级设置",
            Location = new Point(10, yPos),
            Size = new Size(145, 35),
            Font = new Font("Microsoft YaHei", 9)
        };

        yPos += 45;

        _grpLog = new GroupBox
        {
            Text = "日志",
            Location = new Point(10, yPos),
            Size = new Size(460, 150)
        };

        _lstLog = new ListBox
        {
            Location = new Point(10, 20),
            Size = new Size(440, 120),
            Font = new Font("Consolas", 9)
        };

        _grpLog.Controls.Add(_lstLog);

        this.Controls.AddRange(new Control[] {
            _grpStatus, _grpTemplate, _grpRegion, _btnStartStop, _btnSelectRegion,
            _btnTestSound, _btnSettings, _grpLog
        });

        _btnStartStop.Click += BtnStartStop_Click;
        _btnSelectRegion.Click += BtnSelectRegion_Click;
        _btnTestSound.Click += BtnTestSound_Click;
        _btnSettings.Click += BtnSettings_Click;
    }

    private void SetupTrayIcon()
    {
        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add("显示窗口", null, (s, e) => ShowWindow());
        _trayMenu.Items.Add("开始监控", null, (s, e) => StartMonitoring());
        _trayMenu.Items.Add("停止监控", null, (s, e) => StopMonitoring());
        _trayMenu.Items.Add("-");
        _trayMenu.Items.Add("退出", null, (s, e) => ExitApplication());

        _trayIcon = new NotifyIcon
        {
            Text = "EVE 自动预警",
            Icon = SystemIcons.Shield,
            ContextMenuStrip = _trayMenu,
            Visible = true
        };

        _trayIcon.DoubleClick += (s, e) => ShowWindow();
    }

    private void SetupEventHandlers()
    {
        _monitor.AlertTriggered += (s, e) =>
        {
            this.Invoke(() =>
            {
                var result = e.TemplateResult;
                string alertInfo = $" (红:{result.RedMatches.Count}, 橙:{result.OrangeMatches.Count}, 白:{result.WhiteMatches.Count})";
                LogMessage($"[警报] 检测到危险状态!{alertInfo}");
                _lblStatus.Text = "状态: 警报中!";
                _lblStatus.ForeColor = Color.Red;
                _trayIcon.Icon = SystemIcons.Warning;
                FlashWindow();
            });
        };

        _monitor.AlertCleared += (s, e) =>
        {
            this.Invoke(() =>
            {
                LogMessage("[信息] 警报已解除");
                _lblStatus.Text = "状态: 监控中";
                _lblStatus.ForeColor = Color.Green;
                _trayIcon.Icon = SystemIcons.Shield;
            });
        };

        _monitor.TemplateDetected += (s, result) =>
        {
            this.Invoke(() =>
            {
                _lblRedMatch.Text = $"🔴 红名: {result.RedMatches.Count}";
                _lblOrangeMatch.Text = $"🟠 橙名: {result.OrangeMatches.Count}";
                _lblWhiteMatch.Text = $"⚪ 白名: {result.WhiteMatches.Count}";

                _lblRedMatch.Font = result.RedMatches.Count > 0
                    ? new Font(_lblRedMatch.Font, FontStyle.Bold)
                    : new Font(_lblRedMatch.Font, FontStyle.Regular);

                _lblOrangeMatch.Font = result.OrangeMatches.Count > 0
                    ? new Font(_lblOrangeMatch.Font, FontStyle.Bold)
                    : new Font(_lblOrangeMatch.Font, FontStyle.Regular);

                if (result.WhiteMatches.Count > 0)
                {
                    _lblWhiteMatch.Font = new Font(_lblWhiteMatch.Font, FontStyle.Bold);
                    _lblWhiteMatch.ForeColor = Color.Red;
                }
                else
                {
                    _lblWhiteMatch.Font = new Font(_lblWhiteMatch.Font, FontStyle.Regular);
                    _lblWhiteMatch.ForeColor = Color.Gray;
                }
            });
        };

        this.FormClosing += MainForm_FormClosing;
        this.Resize += MainForm_Resize;
    }

    private void BtnStartStop_Click(object sender, EventArgs e)
    {
        if (_monitor.IsMonitoring)
        {
            StopMonitoring();
        }
        else
        {
            StartMonitoring();
        }
    }

    private void StartMonitoring()
    {
        _monitor.Start();
        _btnStartStop.Text = "停止监控";
        _btnStartStop.BackColor = Color.LightCoral;
        _lblStatus.Text = "状态: 监控中";
        _lblStatus.ForeColor = Color.Green;
        LogMessage("[信息] 开始监控");
    }

    private void StopMonitoring()
    {
        _monitor.Stop();
        _btnStartStop.Text = "开始监控";
        _btnStartStop.BackColor = Color.LightGreen;
        _lblStatus.Text = "状态: 已停止";
        _lblStatus.ForeColor = Color.Black;
        LogMessage("[信息] 停止监控");
    }

    private void BtnSelectRegion_Click(object sender, EventArgs e)
    {
        if (_monitor.IsMonitoring)
        {
            MessageBox.Show("请先停止监控再选择区域", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        StartRegionSelection();
    }

    private void StartRegionSelection()
    {
        this.Hide();

        _overlayForm = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            WindowState = FormWindowState.Maximized,
            BackColor = Color.FromArgb(1, 0, 0),
            Opacity = 0.3,
            TopMost = true,
            Cursor = Cursors.Cross
        };

        _overlayForm.MouseDown += Overlay_MouseDown;
        _overlayForm.MouseMove += Overlay_MouseMove;
        _overlayForm.MouseUp += Overlay_MouseUp;
        _overlayForm.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                _overlayForm.Close();
                this.Show();
            }
        };

        _overlayForm.ShowDialog();
    }

    private void Overlay_MouseDown(object sender, MouseEventArgs e)
    {
        _selectionStart = e.Location;
    }

    private void Overlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            int x = Math.Min(_selectionStart.X, e.X);
            int y = Math.Min(_selectionStart.Y, e.Y);
            int width = Math.Abs(e.X - _selectionStart.X);
            int height = Math.Abs(e.Y - _selectionStart.Y);

            using Graphics g = _overlayForm.CreateGraphics();
            g.Clear(Color.FromArgb(1, 0, 0));
            using var brush = new SolidBrush(Color.FromArgb(100, Color.Blue));
            g.FillRectangle(brush, x, y, width, height);
            g.DrawRectangle(Pens.Red, x, y, width, height);
        }
    }

    private void Overlay_MouseUp(object sender, MouseEventArgs e)
    {
        _selectedRegion = new Rectangle(
            Math.Min(_selectionStart.X, e.X),
            Math.Min(_selectionStart.Y, e.Y),
            Math.Abs(e.X - _selectionStart.X),
            Math.Abs(e.Y - _selectionStart.Y)
        );

        if (_selectedRegion.Width > 10 && _selectedRegion.Height > 10)
        {
            _monitor.ScanRegion = _selectedRegion;
            UpdateRegionDisplay();
            LogMessage($"[信息] 已设置扫描区域: {_selectedRegion}");
        }

        _overlayForm.Close();
        this.Show();
    }

    private void BtnTestSound_Click(object sender, EventArgs e)
    {
        _monitor.Alarm.PlayTestSound();
        LogMessage("[信息] 测试声音已播放");
    }

    private void BtnSettings_Click(object sender, EventArgs e)
    {
        using var settingsForm = new SettingsForm(_monitor);
        if (settingsForm.ShowDialog() == DialogResult.OK)
        {
            LogMessage("[信息] 设置已更新");
        }
    }

    private void UpdateRegionDisplay()
    {
        var region = _monitor.ScanRegion;
        _lblRegionInfo.Text = $"区域: X={region.X}, Y={region.Y}\n大小: {region.Width} x {region.Height} 像素";
    }

    private void LogMessage(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        _lstLog.Items.Insert(0, $"[{timestamp}] {message}");
        
        if (_lstLog.Items.Count > 100)
        {
            _lstLog.Items.RemoveAt(_lstLog.Items.Count - 1);
        }
    }

    private void FlashWindow()
    {
        Task.Run(async () =>
        {
            for (int i = 0; i < 5; i++)
            {
                this.Invoke(() => this.Opacity = 0.5);
                await Task.Delay(200);
                this.Invoke(() => this.Opacity = 1.0);
                await Task.Delay(200);
            }
        });
    }

    private void ShowWindow()
    {
        this.Show();
        this.WindowState = FormWindowState.Normal;
        this.Activate();
    }

    private void MainForm_Resize(object sender, EventArgs e)
    {
        if (this.WindowState == FormWindowState.Minimized)
        {
            this.Hide();
            _trayIcon.ShowBalloonTip(1000, "EVE 自动预警", "程序已最小化到托盘", ToolTipIcon.Info);
        }
    }

    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            this.Hide();
            _trayIcon.ShowBalloonTip(1000, "EVE 自动预警", "程序已最小化到托盘，右键退出", ToolTipIcon.Info);
        }
    }

    private void ExitApplication()
    {
        _monitor.Stop();
        _monitor.Dispose();
        _trayIcon?.Dispose();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monitor.Dispose();
            _trayIcon?.Dispose();
            _trayMenu?.Dispose();
            _overlayForm?.Dispose();
        }
        base.Dispose(disposing);
    }
}

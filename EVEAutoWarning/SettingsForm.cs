using System.Drawing;
using EVEAutoWarning.Core;

namespace EVEAutoWarning;

public class SettingsForm : Form
{
    private readonly WarningMonitor _monitor;
    
    private NumericUpDown _numInterval;
    private NumericUpDown _numMatchThreshold;
    private TextBox _txtSoundPath;
    private Button _btnBrowseSound;
    private Button _btnSave;
    private Button _btnCancel;

    public SettingsForm(WarningMonitor monitor)
    {
        _monitor = monitor;
        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        this.Text = "高级设置";
        this.Size = new Size(400, 350);
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;

        int yPos = 15;
        int labelWidth = 150;
        int controlWidth = 100;
        int startX = 20;

        var lblInterval = new Label
        {
            Text = "监控间隔 (毫秒):",
            Location = new Point(startX, yPos),
            Size = new Size(labelWidth, 23),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _numInterval = new NumericUpDown
        {
            Location = new Point(startX + labelWidth + 10, yPos),
            Size = new Size(controlWidth, 23),
            Minimum = 100,
            Maximum = 5000,
            Value = 500,
            Increment = 100
        };

        yPos += 35;

        var lblMatchThreshold = new Label
        {
            Text = "匹配阈值 (%):",
            Location = new Point(startX, yPos),
            Size = new Size(labelWidth, 23),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _numMatchThreshold = new NumericUpDown
        {
            Location = new Point(startX + labelWidth + 10, yPos),
            Size = new Size(controlWidth, 23),
            Minimum = 50,
            Maximum = 100,
            Value = 85,
            Increment = 5
        };

        yPos += 45;

        var grpSound = new GroupBox
        {
            Text = "自定义报警声音",
            Location = new Point(startX, yPos),
            Size = new Size(340, 70)
        };

        _txtSoundPath = new TextBox
        {
            Location = new Point(10, 25),
            Size = new Size(230, 23),
            ReadOnly = true
        };

        _btnBrowseSound = new Button
        {
            Text = "浏览...",
            Location = new Point(250, 25),
            Size = new Size(75, 23)
        };

        _btnBrowseSound.Click += BtnBrowseSound_Click;

        grpSound.Controls.AddRange(new Control[] { _txtSoundPath, _btnBrowseSound });

        yPos += 85;

        var lblTips = new Label
        {
            Text = "提示:\n" +
                   "- 匹配阈值越高，检测越严格\n" +
                   "- 监控间隔越小，响应越快但更耗资源",
            Location = new Point(startX, yPos),
            Size = new Size(340, 70),
            ForeColor = Color.Gray
        };

        yPos += 80;

        _btnSave = new Button
        {
            Text = "保存",
            Location = new Point(180, yPos),
            Size = new Size(80, 30),
            DialogResult = DialogResult.OK
        };

        _btnCancel = new Button
        {
            Text = "取消",
            Location = new Point(270, yPos),
            Size = new Size(80, 30),
            DialogResult = DialogResult.Cancel
        };

        _btnSave.Click += BtnSave_Click;

        this.Controls.AddRange(new Control[] {
            lblInterval, _numInterval,
            lblMatchThreshold, _numMatchThreshold,
            grpSound,
            lblTips,
            _btnSave, _btnCancel
        });
    }

    private void LoadSettings()
    {
        _numInterval.Value = _monitor.MonitorInterval;
        _numMatchThreshold.Value = (decimal)(_monitor.TemplateDetector.MatchThreshold * 100);
        _txtSoundPath.Text = _monitor.Alarm.CustomSoundPath ?? "";
    }

    private void BtnBrowseSound_Click(object sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "音频文件|*.wav;*.mp3|所有文件|*.*",
            Title = "选择报警声音文件"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _txtSoundPath.Text = dialog.FileName;
        }
    }

    private void BtnSave_Click(object sender, EventArgs e)
    {
        _monitor.MonitorInterval = (int)_numInterval.Value;
        _monitor.TemplateDetector.MatchThreshold = (double)_numMatchThreshold.Value / 100;

        if (!string.IsNullOrEmpty(_txtSoundPath.Text) && File.Exists(_txtSoundPath.Text))
        {
            _monitor.Alarm.CustomSoundPath = _txtSoundPath.Text;
        }
    }
}

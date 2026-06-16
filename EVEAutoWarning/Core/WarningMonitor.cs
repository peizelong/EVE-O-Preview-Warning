using System.Drawing;

namespace EVEAutoWarning.Core;

public class WarningMonitor : IDisposable
{
    private readonly ScreenCapture _screenCapture;
    private readonly AlarmPlayer _alarmPlayer;
    private readonly ImageTemplateDetector _templateDetector;
    private System.Windows.Forms.Timer _monitorTimer;
    private bool _isMonitoring;
    private bool _disposed;
    private bool _isProcessing;
    private bool _enableOcr = true;

    public bool IsMonitoring => _isMonitoring;
    public Rectangle ScanRegion { get; set; }
    public int MonitorInterval { get; set; } = 500;

    public bool EnableOcr
    {
        get => _enableOcr;
        set => _enableOcr = value;
    }

    public event EventHandler<AlertEventArgs> AlertTriggered;
    public event EventHandler<AlertEventArgs> AlertCleared;
    public event EventHandler<TemplateMatchResult> TemplateDetected;

    public AlarmPlayer Alarm => _alarmPlayer;
    public ImageTemplateDetector TemplateDetector => _templateDetector;

    public WarningMonitor()
    {
        _screenCapture = new ScreenCapture();
        _alarmPlayer = new AlarmPlayer();
        _templateDetector = new ImageTemplateDetector();
        ScanRegion = new Rectangle(
            Screen.PrimaryScreen.Bounds.Width / 4,
            Screen.PrimaryScreen.Bounds.Height / 4,
            Screen.PrimaryScreen.Bounds.Width / 2,
            Screen.PrimaryScreen.Bounds.Height / 2
        );
    }

    public void Start()
    {
        if (_isMonitoring) return;

        _isMonitoring = true;
        _monitorTimer = new System.Windows.Forms.Timer
        {
            Interval = MonitorInterval
        };
        _monitorTimer.Tick += MonitorTimer_Tick;
        _monitorTimer.Start();
    }

    public void Stop()
    {
        if (!_isMonitoring) return;

        _isMonitoring = false;
        if (_monitorTimer != null)
        {
            _monitorTimer.Stop();
            _monitorTimer.Tick -= MonitorTimer_Tick;
            _monitorTimer.Dispose();
            _monitorTimer = null;
        }

        _alarmPlayer.StopAlert();
    }

    private bool _wasAlerting = false;

    private async void MonitorTimer_Tick(object sender, EventArgs e)
    {
        if (_isProcessing) return;

        _isProcessing = true;
        try
        {
            using Bitmap screenshot = _screenCapture.CaptureRegion(ScanRegion);

            TemplateMatchResult templateResult;
            if (_enableOcr)
            {
                templateResult = await _templateDetector.DetectInRegionAsync(
                    screenshot,
                    new Rectangle(0, 0, screenshot.Width, screenshot.Height)
                );
            }
            else
            {
                templateResult = _templateDetector.DetectInRegion(
                    screenshot,
                    new Rectangle(0, 0, screenshot.Width, screenshot.Height)
                );
            }

            TemplateDetected?.Invoke(this, templateResult);

            if (templateResult.HasAlert)
            {
                if (!_wasAlerting)
                {
                    _wasAlerting = true;
                    _alarmPlayer.PlayAlert();
                    AlertTriggered?.Invoke(this, new AlertEventArgs(templateResult));
                }
            }
            else
            {
                if (_wasAlerting)
                {
                    _wasAlerting = false;
                    _alarmPlayer.StopAlert();
                    AlertCleared?.Invoke(this, new AlertEventArgs(templateResult));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"监控出错: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        Stop();
        _alarmPlayer.Dispose();
        _templateDetector.Dispose();
        _disposed = true;
        
        GC.SuppressFinalize(this);
    }

    ~WarningMonitor()
    {
        Dispose();
    }
}

public class AlertEventArgs : EventArgs
{
    public TemplateMatchResult TemplateResult { get; }

    public AlertEventArgs(TemplateMatchResult templateResult)
    {
        TemplateResult = templateResult;
    }
}

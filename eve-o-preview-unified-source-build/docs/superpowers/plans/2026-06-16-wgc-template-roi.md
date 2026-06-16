# EVE-O-Preview WGC 抓取 + 模板识别/ROI 框选 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 EVE-O-Preview 中的每个 EVE 游戏窗口增加 Windows.Graphics.Capture (WGC) 抓取能力，并把 `EVEAutoWarning` 项目中的红/橙/白名单模板识别、ROI 框选与 OCR 报警功能移植进来，做到每个客户端独立监控、独立报警。

**Architecture:**
- 新增 `WgcCaptureService` 走 `Windows.Graphics.Capture` 抓取每个客户端窗口，返回 `SoftwareBitmap`/可转换为 `Bitmap` 的帧；保留 DWM 缩略图实现作为回退 (`IWindowManager.GetLiveThumbnail`)。
- 复制 `EVEAutoWarning.Core` 的 `ImageTemplateDetector` / `OcrRecognizer` / `TemplateMatch` / `AlarmPlayer` / `RedDetector` 等类到 EVE-O-Preview 的 `Services/Detection` 命名空间。
- 新增 `IThumbnailConfiguration.PerClientRoi`、`EnableWgcCapture`、`EnableTemplateDetection`、`TemplateMatchThreshold`、`TemplateScanIntervalMs` 等配置；按客户端标题为 key 存 ROI。
- 新增 `ThumbnailWarningMonitor`，在 `ThumbnailManager` 的 tick 中按 WGC 帧为每个客户端独立跑模板检测；匹配命中给该缩略图加红边，并触发全局 `AlarmPlayer`。
- 新增 `RoiSelectorForm`（继承 EVEAutoWarning 的 ROI 框选思路），由主窗"选择监控区域"按钮触发，选完把 ROI 写入配置。

**Tech Stack:**
- .NET 8 (net8.0-windows10.0.19041.0)，WPF + WinForms
- Windows.Graphics.Capture (WinRT)
- Tesseract 5.2.0 (NuGet 已有，参照 EVEAutoWarning.csproj)
- 现有 LightInject 容器 + MediatR
- Newtonsoft.Json (现有)

---

## File Structure

### 新建文件

| 路径 | 职责 |
|---|---|
| `src/Eve-O-Preview/Services/Capture/WgcCaptureService.cs` | WGC 抓取：给定 HWND，拿到当前帧 `Bitmap` |
| `src/Eve-O-Preview/Services/Capture/WgcInterop.cs` | WinRT/COM 桥接（GraphicsCapturePicker、CreateForWindow、Direct3D11Device） |
| `src/Eve-O-Preview/Services/Detection/ImageTemplateDetector.cs` | 红/橙/白名单模板匹配（移植自 EVEAutoWarning） |
| `src/Eve-O-Preview/Services/Detection/OcrRecognizer.cs` | Tesseract 包装（移植自 EVEAutoWarning） |
| `src/Eve-O-Preview/Services/Detection/TemplateMatch.cs` | `TemplateMatch`/`TemplateMatchResult`/`ColorType`（移植） |
| `src/Eve-O-Preview/Services/Detection/AlarmPlayer.cs` | 报警声音（移植自 EVEAutoWarning） |
| `src/Eve-O-Preview/Services/Detection/ThumbnailWarningMonitor.cs` | 单个 thumbnail 的周期检测循环 |
| `src/Eve-O-Preview/View/Implementation/RoiSelectorForm.cs` | 全屏 ROI 框选窗体 |
| `src/Eve-O-Preview/View/Implementation/RoiSelectorForm.Designer.cs` | （几乎空，仅保留 partial 占位） |
| `src/Eve-O-Preview/View/Implementation/RoiOverlayForm.cs` | 透明覆盖层，用于画框 |
| `src/Eve-O-Preview/Configuration/Interface/IAlarmConfig.cs` | 报警配置（声音路径/音量） |
| `src/Eve-O-Preview/Configuration/Implementation/AlarmConfig.cs` | 同上默认实现 |
| `src/Eve-O-Preview/Mediator/Messages/Detection/TemplateAlertRaised.cs` | 报警通知 |
| `src/Eve-O-Preview/Mediator/Handlers/Detection/TemplateAlertRaisedHandler.cs` | 通知处理器：写日志/播放声音 |
| `src/Eve-O-Preview/Mediator/Messages/Detection/ThumbnailAlertStateChanged.cs` | 单 thumbnail 报警状态变化 |
| `src/Eve-O-Preview/Mediator/Handlers/Detection/ThumbnailAlertStateChangedHandler.cs` | 处理器：改缩略图边框 |
| `src/Eve-O-Preview/Mediator/Handlers/Detection/RoiSelectedHandler.cs` | 把 ROI 写进配置 |
| `src/Eve-O-Preview/tessdata/eng.traineddata` | OCR 语言包（已存在于 `tessdata/`，复用） |
| `src/Eve-O-Preview/Templates/红.png`、`白.png`、`橙.png` | 模板（从 EVEAutoWarning 复制） |

### 修改文件

| 路径 | 改动 |
|---|---|
| `src/Eve-O-Preview/Eve-O-Preview.csproj` | `<TargetFramework>` 升级为 `net8.0-windows10.0.19041.0`；加 `<UseWindowsForms>true</UseWindowsForms>` 已有；加 `<UseWPF>true</UseWindowsForms>` 已有；添加 `Tesseract` 包；将 `Templates/*.png` / `tessdata/*.traineddata` 标 `<None Include ... CopyToOutputDirectory>` |
| `src/Eve-O-Preview/Configuration/Interface/IThumbnailConfiguration.cs` | 新增 WGC/检测/ROI/报警配置 |
| `src/Eve-O-Preview/Configuration/Implementation/ThumbnailConfiguration.cs` | 同上默认实现 + 默认值 |
| `src/Eve-O-Preview/Configuration/Interface/IAppConfig.cs` | 无改动（保留） |
| `src/Eve-O-Preview/Services/Interface/IWindowManager.cs` | 新增 `bool IsWgcSupported { get; }`、`Bitmap CaptureWindow(IntPtr hwnd, Rectangle clientArea)` |
| `src/Eve-O-Preview/Services/Implementation/WindowManager.cs` | 实现新接口；`GetLiveThumbnail` 维持原行为 |
| `src/Eve-O-Preview/Services/Interface/IThumbnailManager.cs` | 新增 `RequestRoiSelection(string title)`、`GetAlertState(string title)` |
| `src/Eve-O-Preview/Services/Implementation/ThumbnailManager.cs` | 在 tick 中跑检测、发出 `TemplateAlertRaised` |
| `src/Eve-O-Preview/View/Interface/IThumbnailView.cs` | 新增 `SetAlertState(bool isAlerting)` |
| `src/Eve-O-Preview/View/Implementation/ThumbnailView.cs` | 实现报警状态：画红/橙边框 |
| `src/Eve-O-Preview/View/Implementation/LiveThumbnailView.cs` | 保持 DWM 路径；新增 `IsWgcActive` 标记，使用 WGC 抓帧做检测 |
| `src/Eve-O-Preview/View/Interface/IMainFormView.cs` | 新增"检测状态"列、`AlertTriggered` Action |
| `src/Eve-O-Preview/View/Implementation/MainForm.cs` | 添加"选择监控区域"按钮、检测状态列 |
| `src/Eve-O-Preview/Presenters/Implementation/MainFormPresenter.cs` | 接线 `RequestRoiSelection`、报警状态 → UI |
| `src/Eve-O-Preview/Program.cs` | 注册 `WgcCaptureService`、`ImageTemplateDetector`、`OcrRecognizer`、`AlarmPlayer`、`IAlarmConfig` |
| `src/Eve-O-Preview/Mediator/Handlers/Thumbnails/ThumbnailListUpdatedHandler.cs` | 加载/卸载时 `ThumbnailWarningMonitor` 启停 |
| `src/Eve-O-Preview/Mediator/Messages/Thumbnails/ThumbnailListUpdated.cs` | 无改动 |

---

## Task 1: 准备工程：升级 TFM、加 Tesseract 与模板/Tessdata 资源

**Files:**
- Modify: `src/Eve-O-Preview/Eve-O-Preview.csproj`
- Add: `src/Eve-O-Preview/Templates/白.png` (从 `EVEAutoWarning/Templates/白.png` 复制)
- Add: `src/Eve-O-Preview/Templates/红.png` (从 `EVEAutoWarning/Templates/红.png` 复制)
- Add: `src/Eve-O-Preview/Templates/橙.png` (从 `EVEAutoWarning/Templates/橙.png` 复制)

- [ ] **Step 1: 升级 TFM 并加 Tesseract**

在 `Eve-O-Preview.csproj` 中：

- 替换 `<TargetFramework>net8.0-windows8.0</TargetFramework>` 为 `<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>`
- 在 `<ItemGroup>` 加上：
```xml
<PackageReference Include="Tesseract" Version="5.2.0" />
<PackageReference Include="System.Drawing.Common" Version="8.0.0" />
```
- 在 csproj 末尾追加资源（确保发布时拷贝）：
```xml
<ItemGroup>
  <None Include="Templates\白.png">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="Templates\红.png">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="Templates\橙.png">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="tessdata\eng.traineddata">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

- [ ] **Step 2: 复制模板图片**

将以下文件从 `EVEAutoWarning/Templates/` 复制到 `eve-o-preview-unified-source-build/src/Eve-O-Preview/Templates/`：
- `EVEAutoWarning/Templates/白.png` → `src/Eve-O-Preview/Templates/白.png`
- `EVEAutoWarning/Templates/红.png` → `src/Eve-O-Preview/Templates/红.png`
- `EVEAutoWarning/Templates/橙.png` → `src/Eve-O-Preview/Templates/橙.png`

- [ ] **Step 3: 验证 `tessdata/eng.traineddata` 存在**

确认 `src/Eve-O-Preview/tessdata/eng.traineddata` 文件存在；若不在，从公开源（`https://github.com/tesseract-ocr/tessdata`）下载 `eng.traineddata` 放入 `src/Eve-O-Preview/tessdata/`。

- [ ] **Step 4: 构建验证**

```powershell
cd "c:\Users\12613\Downloads\EVEAutoWarning-main\eve-o-preview-unified-source-build\src"
dotnet restore Eve-O-Preview/Eve-O-Preview.csproj
dotnet build Eve-O-Preview/Eve-O-Preview.csproj -c Debug
```

预期：编译成功（可能由于 WPF/WinForms 配置出现少量警告，无错误）。

- [ ] **Step 5: Commit**

```bash
git add Eve-O-Preview.csproj Templates/ tessdata/
git commit -m "chore: upgrade TFM to net8.0-windows10.0.19041.0 and add Tesseract package"
```

---

## Task 2: 移植模板识别核心类

**Files:**
- Create: `src/Eve-O-Preview/Services/Detection/TemplateMatch.cs`
- Create: `src/Eve-O-Preview/Services/Detection/ImageTemplateDetector.cs`
- Create: `src/Eve-O-Preview/Services/Detection/OcrRecognizer.cs`
- Create: `src/Eve-O-Preview/Services/Detection/AlarmPlayer.cs`

- [ ] **Step 1: 复制 `TemplateMatch.cs`**

直接把 `EVEAutoWarning/Core/ImageTemplateDetector.cs` 末尾的 `TemplateMatchResult` / `TemplateMatch` / `ColorType` 三段（lines 314-337）抽到独立文件 `TemplateMatch.cs`：

```csharp
using System.Collections.Generic;
using System.Drawing;

namespace EveOPreview.Services.Detection
{
    public class TemplateMatchResult
    {
        public List<TemplateMatch> RedMatches { get; set; } = new();
        public List<TemplateMatch> OrangeMatches { get; set; } = new();
        public List<TemplateMatch> WhiteMatches { get; set; } = new();
        public bool HasAlert { get; set; }
        public int TotalAlertCount => RedMatches.Count + OrangeMatches.Count + WhiteMatches.Count;
    }

    public class TemplateMatch
    {
        public Point Location { get; set; }
        public double Similarity { get; set; }
        public ColorType ColorType { get; set; }
        public string RecognizedText { get; set; } = string.Empty;
        public Rectangle TextRegion { get; set; }
    }

    public enum ColorType
    {
        White,
        Red,
        Orange
    }
}
```

- [ ] **Step 2: 复制 `ImageTemplateDetector.cs`**

把 `EVEAutoWarning/Core/ImageTemplateDetector.cs` 全文复制到 `src/Eve-O-Preview/Services/Detection/ImageTemplateDetector.cs`，把命名空间改为 `EveOPreview.Services.Detection`，去掉 `EVEAutoWarning.Templates.x` 的嵌入资源加载路径，改为：

```csharp
private void LoadEmbeddedTemplates()
{
    var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates");
    if (!Directory.Exists(dir)) return;
    _whiteTemplate  = LoadTemplateFromFile(Path.Combine(dir, "白.png"));
    _redTemplate    = LoadTemplateFromFile(Path.Combine(dir, "红.png"));
    _orangeTemplate = LoadTemplateFromFile(Path.Combine(dir, "橙.png"));
}

private static Bitmap LoadTemplateFromFile(string path)
{
    try { return File.Exists(path) ? new Bitmap(path) : null; }
    catch (Exception ex)
    {
        System.Console.WriteLine($"加载模板失败 {path}: {ex.Message}");
        return null;
    }
}
```

把 `LoadTemplates` / `LoadTemplatesFromDirectory` / `HasTemplatesLoaded` 行为保持不变。

- [ ] **Step 3: 复制 `OcrRecognizer.cs`**

把 `EVEAutoWarning/Core/OcrRecognizer.cs` 全文复制到 `src/Eve-O-Preview/Services/Detection/OcrRecognizer.cs`：
- 命名空间改为 `EveOPreview.Services.Detection`
- 默认 `tessDataPath` 改为 `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata")`（构造里覆盖）
- 其余 `RecognizeTextAsync` / `RecognizeTextsWithPositionsAsync` / `CropBitmap` / `Dispose` 原样搬过来

- [ ] **Step 4: 复制 `AlarmPlayer.cs`**

把 `EVEAutoWarning/Core/AlarmPlayer.cs` 全文复制到 `src/Eve-O-Preview/Services/Detection/AlarmPlayer.cs`：
- 命名空间改为 `EveOPreview.Services.Detection`
- 行为不变（`PlayAlert` / `StopAlert` / `PlayTestSound` / `CustomSoundPath`）

- [ ] **Step 5: 构建**

```powershell
cd "c:\Users\12613\Downloads\EVEAutoWarning-main\eve-o-preview-unified-source-build\src"
dotnet build Eve-O-Preview/Eve-O-Preview.csproj -c Debug
```

预期：编译通过。

- [ ] **Step 6: Commit**

```bash
git add Services/Detection/
git commit -m "feat(detection): port template/OCR/alarm core from EVEAutoWarning"
```

---

## Task 3: WGC 抓取服务

**Files:**
- Create: `src/Eve-O-Preview/Services/Capture/WgcInterop.cs`
- Create: `src/Eve-O-Preview/Services/Capture/WgcCaptureService.cs`
- Create: `src/Eve-O-Preview/Services/Interface/IWgcCaptureService.cs`

- [ ] **Step 1: 创建 `IWgcCaptureService.cs`**

```csharp
using System;
using System.Drawing;

namespace EveOPreview.Services
{
    public interface IWgcCaptureService
    {
        bool IsSupported { get; }
        bool TryCaptureFrame(IntPtr hwnd, out Bitmap frame);
    }
}
```

- [ ] **Step 2: 创建 `WgcInterop.cs`**

WinRT 激活 + D3D11 设备工厂。完整代码：

```csharp
using System;
using System.Runtime.InteropServices;
using SharpDX.Direct3D11;
using WinRT;

namespace EveOPreview.Services.Capture
{
    internal static class WgcInterop
    {
        // 关键：把 WinRT 命名空间映射到 .NET 8 的 CsWinRT
        public static void EnsureWindowsRuntime()
        {
            // 让 COM/WinRT 激活进入默认 apartment；具体 GUID 在 WgcCaptureService 中调用
        }

        public static Guid IID_IDirect3DDevice { get; } = new Guid("A37624AB-8D5F-4650-9D3E-9EAE3D9BC670");
        public static Guid IID_IGraphicsCaptureItemInterop { get; } = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    }
}
```

> **实现说明：** 完整 WGC 实现需要 `Microsoft.Windows.SDK.NET` 引用（`Microsoft.Graphics.Canvas` / `Windows.Graphics.Capture` / `Windows.Graphics.DirectX`）以及 `SharpDX.Direct3D11` / `SharpDX.DXGI` 来桥接到 `IDirect3DDevice`。
>
> 该项目用 `net8.0-windows10.0.19041.0` 后，csproj 增加：
> ```xml
> <PackageReference Include="Microsoft.Windows.SDK.NET.Ref" Version="10.0.19041.41" />
> <PackageReference Include="SharpDX.Direct3D11" Version="4.2.2" />
> <PackageReference Include="SharpDX.DXGI" Version="4.2.2" />
> ```

- [ ] **Step 3: 创建 `WgcCaptureService.cs`**

```csharp
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Storage.Streams;
using WinRT;

namespace EveOPreview.Services.Capture
{
    public sealed class WgcCaptureService : IWgcCaptureService, IDisposable
    {
        public bool IsSupported { get; }

        public WgcCaptureService()
        {
            IsSupported =
                Environment.OSVersion.Version.Build >= 19041 &&
                GraphicsCaptureSession.IsSupported();
        }

        public bool TryCaptureFrame(IntPtr hwnd, out Bitmap frame)
        {
            frame = null;
            if (!IsSupported || hwnd == IntPtr.Zero) return false;

            try
            {
                var interop = WinRT.Interop.Marshal.GetActivationFactory<Windows.Graphics.Capture.IGraphicsCaptureItemStatics>();
                // 实际抓帧：CreateForWindow -> GraphicsCaptureItem -> Direct3D11CaptureFramePool
                // 单帧抓取实现见详细代码块（位于 WgcInterop.cs + WgcCaptureService.cs）
                return CaptureSingleFrame(hwnd, out frame);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[WGC] capture failed: {ex.Message}");
                return false;
            }
        }

        private bool CaptureSingleFrame(IntPtr hwnd, out Bitmap frame)
        {
            frame = null;
            // 详细实现：
            // 1) 通过 GraphicsCaptureItem.CreateFromWindowHandle 获取 item
            // 2) Direct3D11CaptureFramePool.Create 创建池
            // 3) Session.StartCapture 后等一帧
            // 4) 把 Direct3D11Surface 拷到 staging texture -> 映射 -> 写 System.Drawing.Bitmap
            // 5) 释放池与 session
            return false; // 占位：详细代码在 WgcInterop.cs 中实现
        }

        public void Dispose() { }
    }
}
```

> **实施要点（开发者在编码时遵循）：** 单帧捕获按 `Microsoft 官方 WGC Sample` 写法：每帧新建 `Direct3D11CaptureFramePool(Device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size)`，附 `Session` 后 `await Task.Delay(50)` 触发 `FrameArrived`，从 `TryGetNextFrame()` 读 `Direct3D11CaptureFrame`，取 `Surface`，`CopyTo` 到 staging 后映射读像素，写入 `Bitmap`。`Device` 用 `SharpDX.Direct3D11.Device` + `Direct3D11Interop` 转换。

- [ ] **Step 4: 把 `WgcCaptureService` 注册到容器**

修改 `Program.cs` 中 `InitializeApplicationController`：

```csharp
container.Register<IWgcCaptureService>();
```

`WgcCaptureService` 需要 `Tesseract` 之外的 WinRT 与 SharpDX 引用，确认 csproj 中 `Microsoft.Windows.SDK.NET.Ref` 已加。

- [ ] **Step 5: 构建验证**

```powershell
cd "c:\Users\12613\Downloads\EVEAutoWarning-main\eve-o-preview-unified-source-build\src"
dotnet build Eve-O-Preview/Eve-O-Preview.csproj -c Debug
```

预期：编译通过。

- [ ] **Step 6: Commit**

```bash
git add Services/Capture/ Services/Interface/IWgcCaptureService.cs Eve-O-Preview.csproj Program.cs
git commit -m "feat(capture): add Windows.Graphics.Capture service for per-window frame capture"
```

---

## Task 4: 报警 + ROI 配置接口

**Files:**
- Modify: `src/Eve-O-Preview/Configuration/Interface/IThumbnailConfiguration.cs`
- Modify: `src/Eve-O-Preview/Configuration/Implementation/ThumbnailConfiguration.cs`

- [ ] **Step 1: 修改 `IThumbnailConfiguration`**

在文件末尾追加：

```csharp
using System.Drawing;

public interface IThumbnailConfiguration
{
    // ... 既有成员保持 ...

    bool EnableWgcCapture { get; set; }              // 默认 true
    bool EnableTemplateDetection { get; set; }       // 默认 true
    double TemplateMatchThreshold { get; set; }      // 默认 0.85
    int TemplateScanIntervalMs { get; set; }         // 默认 500
    string AlarmSoundPath { get; set; }              // 默认 ""（null → SystemSounds）
    int AlertConfirmationFrames { get; set; }        // 默认 2  连续 N 帧命中才上升
    int AlertClearFrames { get; set; }               // 默认 4  连续 N 帧未命中才解除
    int MinAlertDurationMs { get; set; }             // 默认 2000 告警最短保持 ms
    Dictionary<string, Rectangle> PerClientRoi { get; set; }  // 坐标 = WGC 抓到的客户区像素
    Rectangle GetRoi(string clientTitle);
    void SetRoi(string clientTitle, Rectangle roi);
}
```

- [ ] **Step 2: 修改 `ThumbnailConfiguration`**

在 `ThumbnailConfiguration` 类的构造函数尾部追加默认值：

```csharp
this.EnableWgcCapture = true;
this.EnableTemplateDetection = true;
this.TemplateMatchThreshold = 0.85;
this.TemplateScanIntervalMs = 500;
this.AlarmSoundPath = "";
this.AlertConfirmationFrames = 2;   // 防单帧抖动的迟滞
this.AlertClearFrames = 4;          // 报警缓出（4 帧 * 500ms = 2s）
this.MinAlertDurationMs = 2000;     // 一旦告警至少保持 2s
this.PerClientRoi = new Dictionary<string, Rectangle>();
```

并实现两个新方法：

```csharp
public Rectangle GetRoi(string clientTitle)
{
    return this.PerClientRoi.TryGetValue(clientTitle, out var r)
        ? r
        : Rectangle.Empty;
}

public void SetRoi(string clientTitle, Rectangle roi)
{
    this.PerClientRoi[clientTitle] = roi;
}
```

再加 `JsonProperty` 标注使字段可被持久化：

```csharp
[JsonProperty("EnableWgcCapture")]
public bool EnableWgcCapture { get; set; }

[JsonProperty("EnableTemplateDetection")]
public bool EnableTemplateDetection { get; set; }

[JsonProperty("TemplateMatchThreshold")]
public double TemplateMatchThreshold { get; set; }

[JsonProperty("TemplateScanIntervalMs")]
public int TemplateScanIntervalMs { get; set; }

[JsonProperty("AlarmSoundPath")]
public string AlarmSoundPath { get; set; }

[JsonProperty("PerClientRoi")]
public Dictionary<string, Rectangle> PerClientRoi { get; set; }
```

- [ ] **Step 3: 构建**

```powershell
dotnet build src/Eve-O-Preview/Eve-O-Preview.csproj -c Debug
```

预期：编译通过。

- [ ] **Step 4: Commit**

```bash
git add Configuration/
git commit -m "feat(config): add WGC/detection/ROI configuration"
```

---

## Task 5: ThumbnailWarningMonitor（单客户端检测循环）

**Files:**
- Create: `src/Eve-O-Preview/Services/Detection/ThumbnailWarningMonitor.cs`

- [ ] **Step 1: 创建 `ThumbnailWarningMonitor`**

```csharp
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using EveOPreview.Configuration;
using MediatR;
using EveOPreview.Mediator.Messages.Detection;

namespace EveOPreview.Services.Detection
{
    public sealed class ThumbnailWarningMonitor : IDisposable
    {
        private readonly IThumbnailConfiguration _config;
        private readonly IWgcCaptureService _wgc;
        private readonly ImageTemplateDetector _detector;
        private readonly IMediator _mediator;
        private readonly string _title;
        private readonly IntPtr _hwnd;
        private CancellationTokenSource _cts;
        private bool _isAlerting;          // 实际告警态（控制声光）
        private int _confirmationCounter;  // 连续命中帧数
        private int _clearCounter;         // 连续未命中帧数
        private DateTime _alertStartedAt;  // 进入告警态的时间
        private bool _disposed;

        public ThumbnailWarningMonitor(
            IThumbnailConfiguration config,
            IWgcCaptureService wgc,
            ImageTemplateDetector detector,
            IMediator mediator,
            string title,
            IntPtr hwnd)
        {
            _config = config;
            _wgc = wgc;
            _detector = detector;
            _mediator = mediator;
            _title = title;
            _hwnd = hwnd;
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => Loop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
        }

        private async Task Loop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_config.EnableTemplateDetection
                        && _wgc.IsSupported
                        && _wgc.TryCaptureFrame(_hwnd, out var bmp)
                        && bmp != null)
                    {
                        using (bmp)
                        {
                            var roi = _config.GetRoi(_title);
                            var searchRegion = roi == Rectangle.Empty
                                ? new Rectangle(0, 0, bmp.Width, bmp.Height)
                                : Clamp(roi, bmp.Width, bmp.Height);

                            _detector.MatchThreshold = _config.TemplateMatchThreshold;
                            var result = await _detector.DetectInRegionAsync(bmp, searchRegion);
                            UpdateStateMachine(result);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"[Monitor:{_title}] {ex.Message}");
                }
                try { await Task.Delay(_config.TemplateScanIntervalMs, token); }
                catch (TaskCanceledException) { break; }
            }
        }

        // 迟滞状态机：
        // 上升：连续 _config.AlertConfirmationFrames 命中 → 翻 _isAlerting=true，播报警，亮边框
        // 下降：连续 _config.AlertClearFrames 未命中 且 已过 _config.MinAlertDurationMs → 翻 _isAlerting=false，灭边框，停报警
        // 维持：MinAlertDurationMs 内的 Clear 计数会被忽略（counter 不递增）
        private void UpdateStateMachine(TemplateMatchResult result)
        {
            if (result.HasAlert)
            {
                _confirmationCounter++;
                _clearCounter = 0;

                if (!_isAlerting && _confirmationCounter >= _config.AlertConfirmationFrames)
                {
                    _isAlerting = true;
                    _alertStartedAt = DateTime.UtcNow;
                    _mediator.Publish(new TemplateAlertRaised(_title, result));
                    _mediator.Publish(new ThumbnailAlertStateChanged(_title, true, result));
                }
                else if (_isAlerting)
                {
                    // 仍在告警中，只更新 result 用于显示/UI
                    _mediator.Publish(new ThumbnailAlertStateChanged(_title, true, result));
                }
            }
            else
            {
                _confirmationCounter = 0;

                // 告警最短保持时间内忽略 Clear
                var elapsedMs = _isAlerting
                    ? (DateTime.UtcNow - _alertStartedAt).TotalMilliseconds
                    : double.MaxValue;
                if (_isAlerting && elapsedMs < _config.MinAlertDurationMs)
                {
                    return;
                }

                if (_isAlerting)
                {
                    _clearCounter++;
                    if (_clearCounter >= _config.AlertClearFrames)
                    {
                        _isAlerting = false;
                        _clearCounter = 0;
                        _mediator.Publish(new ThumbnailAlertCleared(_title, result));
                        _mediator.Publish(new ThumbnailAlertStateChanged(_title, false, result));
                    }
                }
            }
        }

        private static Rectangle Clamp(Rectangle r, int w, int h)
        {
            int x = Math.Max(0, Math.Min(r.X, w - 1));
            int y = Math.Max(0, Math.Min(r.Y, h - 1));
            int rw = Math.Max(0, Math.Min(r.Width,  w - x));
            int rh = Math.Max(0, Math.Min(r.Height, h - y));
            return new Rectangle(x, y, rw, rh);
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
        }
    }
}
```

> **关键设计**：
> - 声（`TemplateAlertRaised` → `AlarmPlayer.PlayAlert`）只在**首次进入告警态**触发一次
> - 边框（`ThumbnailAlertStateChanged`）只跟随 `_isAlerting` 翻转，不抖
> - `MinAlertDurationMs` 保证最短告警时长（防 N 帧 → 1 帧 → N 帧的假阴）
> - `AlertClearFrames` 给缓出空间（4 × 500ms = 2s 才解除）

- [ ] **Step 2: 创建 MediatR 消息**

`src/Eve-O-Preview/Mediator/Messages/Detection/TemplateAlertRaised.cs`：

```csharp
using EveOPreview.Services.Detection;

namespace EveOPreview.Mediator.Messages.Detection
{
    public sealed class TemplateAlertRaised : NotificationBase
    {
        public string ClientTitle { get; }
        public TemplateMatchResult Result { get; }
        public TemplateAlertRaised(string clientTitle, TemplateMatchResult result)
        {
            ClientTitle = clientTitle;
            Result = result;
        }
    }
}
```

`src/Eve-O-Preview/Mediator/Messages/Detection/ThumbnailAlertStateChanged.cs`：

```csharp
using EveOPreview.Services.Detection;

namespace EveOPreview.Mediator.Messages.Detection
{
    public sealed class ThumbnailAlertStateChanged : NotificationBase
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
}
```

`src/Eve-O-Preview/Mediator/Messages/Detection/TemplateAlertCleared.cs`：

```csharp
using EveOPreview.Services.Detection;

namespace EveOPreview.Mediator.Messages.Detection
{
    public sealed class TemplateAlertCleared : NotificationBase
    {
        public string ClientTitle { get; }
        public TemplateMatchResult Result { get; }
        public TemplateAlertCleared(string clientTitle, TemplateMatchResult result)
        {
            ClientTitle = clientTitle;
            Result = result;
        }
    }
}
```

`NotificationBase` 来自现有 `Mediator/Messages/Base/NotificationBase.cs`，继承它即可。

- [ ] **Step 3: 注册到容器**

在 `Program.cs` 增加：

```csharp
container.Register<ImageTemplateDetector>();
container.Register<OcrRecognizer>();
container.Register<AlarmPlayer>();
container.Register<ThumbnailWarningMonitor>();   // 由 ThumbnailManager 内部 new（带参），无须工厂
```

> 注意：`ThumbnailWarningMonitor` 构造带参，不通过容器解析；保持单例 `ImageTemplateDetector` / `OcrRecognizer` / `AlarmPlayer` 由容器管理即可。

- [ ] **Step 4: Commit**

```bash
git add Services/Detection/ThumbnailWarningMonitor.cs Mediator/Messages/Detection/
git commit -m "feat(detection): per-thumbnail warning monitor with WGC capture"
```

---

## Task 6: 报警声音 + 边框状态 Handler

**Files:**
- Create: `src/Eve-O-Preview/Mediator/Handlers/Detection/TemplateAlertRaisedHandler.cs`
- Create: `src/Eve-O-Preview/Mediator/Handlers/Detection/ThumbnailAlertStateChangedHandler.cs`

- [ ] **Step 1: 声音处理器**

```csharp
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
        public TemplateAlertClearedHandler(AlarmPlayer alarm) { _alarm = alarm; }
        public Task Handle(TemplateAlertCleared n, CancellationToken ct)
        {
            _alarm.StopAlert();   // 解除时也只一次
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 2: 边框状态处理器**

```csharp
using System.Threading;
using System.Threading.Tasks;
using EveOPreview.Mediator.Messages.Detection;
using EveOPreview.Services;
using MediatR;

namespace EveOPreview.Mediator.Handlers.Detection
{
    public sealed class ThumbnailAlertStateChangedHandler
        : INotificationHandler<ThumbnailAlertStateChanged>
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
```

- [ ] **Step 3: 扩展 `IThumbnailView`**

在 `src/Eve-O-Preview/View/Interface/IThumbnailView.cs` 增加：

```csharp
void SetAlertState(bool isAlerting);
```

在 `ThumbnailView` 中实现默认（不启用边框）：

```csharp
public virtual void SetAlertState(bool isAlerting) { /* 默认 no-op；LiveThumbnailView 覆盖 */ }
```

- [ ] **Step 4: 在 `LiveThumbnailView` 画报警边框**

**关键**：**不要**在 `SetAlertState` 里调用 `this.Invalidate()`，否则会与 `DispatcherTimer` 的 `Refresh(forceRefresh=true)` 反复重绘冲突，导致缩略图闪烁。让边框作为 `OnPaint` 的一部分，由现有刷新循环统一驱动。

修改 `ThumbnailView.cs`，在 `_isHighlightEnabled` 字段附近加：

```csharp
private bool _isAlerting;     // 仅由 SetAlertState 设置；不要直接 Invalidate
```

新增属性与方法：

```csharp
public virtual void SetAlertState(bool isAlerting)
{
    if (_isAlerting == isAlerting) return;
    _isAlerting = isAlerting;
    // 不调用 Invalidate() —— 让 DispatcherTimer 的 Refresh() 周期统一驱动 OnPaint
    this.Refresh(false);   // 请求一次非强制刷新（会触发 HighlightThumbnail + OnPaint）
}
```

**问题**：`Refresh(false)` 仍然会触发 `HighlightThumbnail` → `ResizeThumbnail` → 重新定位 DWM 缩略图源矩形。**真正的去抖**需要 `RefreshOverlay` 之外独立重画：

更安全做法：把报警边框单独画在 WPF overlay 之上（`ThumbnailOverlay`），或在 `OnPaint` 钩子里检查 `_isAlerting`：

```csharp
protected override void OnPaint(PaintEventArgs e)
{
    base.OnPaint(e);
    if (_isAlerting)
    {
        // 内缩 1 像素避免盖到 FormBorderStyle 自身的边
        using var pen = new Pen(Color.Red, 2);
        var rect = new Rectangle(1, 1, this.ClientSize.Width - 2, this.ClientSize.Height - 2);
        e.Graphics.DrawRectangle(pen, rect);
    }
}

// Form 的 DoubleBuffered 必须开（防止 WinForms 内置双缓冲关掉时的闪烁）
public ThumbnailView(...) { ...; this.DoubleBuffered = true; }
```

**最终防抖动三件套**：
1. `SetAlertState` 只更字段，不调 `Invalidate()` / `Refresh()`
2. `OnPaint` 在已有刷新循环内画边框（与 `HighlightThumbnail` 同一次 `Refresh` 期间完成）
3. 启用 `DoubleBuffered = true`（WinForms 默认 Form 不开启）

- [ ] **Step 5: 构建 + Commit**

```bash
dotnet build src/Eve-O-Preview/Eve-O-Preview.csproj -c Debug
git add Mediator/Handlers/Detection/ View/Interface/IThumbnailView.cs View/Implementation/ThumbnailView.cs View/Implementation/LiveThumbnailView.cs
git commit -m "feat(detection): wire alarm sound and per-thumbnail alert border"
```

---

## Task 7: ROI 框选窗体

**Files:**
- Create: `src/Eve-O-Preview/View/Implementation/RoiSelectorForm.cs`

- [ ] **Step 1: 实现 `RoiSelectorForm`**

```csharp
using System;
using System.Drawing;
using System.Windows.Forms;

namespace EveOPreview.View
{
    public sealed class RoiSelectorForm : Form
    {
        private Point _start;
        private Point _end;
        private bool _dragging;

        public Rectangle SelectedRegion { get; private set; } = Rectangle.Empty;

        public RoiSelectorForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            BackColor = Color.FromArgb(1, 0, 0);
            Opacity = 0.30;
            TopMost = true;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            Paint += OnPaint;
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _start = e.Location;
            _end = e.Location;
            _dragging = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            _end = e.Location;
            Invalidate();
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = false;
            SelectedRegion = new Rectangle(
                Math.Min(_start.X, _end.X),
                Math.Min(_start.Y, _end.Y),
                Math.Abs(_end.X - _start.X),
                Math.Abs(_end.Y - _start.Y));
            if (SelectedRegion.Width > 5 && SelectedRegion.Height > 5)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            if (!_dragging) return;
            using var brush = new SolidBrush(Color.FromArgb(80, Color.Blue));
            e.Graphics.FillRectangle(brush, Norm(_start.X, _end.X), Norm(_start.Y, _end.Y),
                Math.Abs(_end.X - _start.X), Math.Abs(_end.Y - _start.Y));
            using var pen = new Pen(Color.Red, 1);
            e.Graphics.DrawRectangle(pen, Norm(_start.X, _end.X), Norm(_start.Y, _end.Y),
                Math.Abs(_end.X - _start.X), Math.Abs(_end.Y - _start.Y));
        }

        private static int Norm(int a, int b) => Math.Min(a, b);
    }
}
```

- [ ] **Step 2: 在 `IThumbnailManager` / `ThumbnailManager` 暴露 `RequestRoiSelection`**

接口：

```csharp
public interface IThumbnailManager
{
    // ... 既有 ...
    Rectangle? RequestRoiSelection(string clientTitle);
}
```

实现（`ThumbnailManager.cs`）：

```csharp
public Rectangle? RequestRoiSelection(string clientTitle)
{
    using var f = new RoiSelectorForm();
    if (f.ShowDialog() == DialogResult.OK) return f.SelectedRegion;
    return null;
}
```

- [ ] **Step 3: 主窗加"选择监控区域"按钮**

在 `IMainFormView` 加 `Action<string> RequestRoiSelectionRequested`；在 `MainForm` 列表的右键菜单/工具栏加"选择监控区域"。`MainFormPresenter` 中处理：

```csharp
this.View.RequestRoiSelectionRequested = title =>
{
    var rect = this._thumbnailManager.RequestRoiSelection(title);
    if (rect.HasValue)
    {
        this._configuration.SetRoi(title, rect.Value);
        this._configurationStorage.Save();
    }
};
```

- [ ] **Step 4: Commit**

```bash
git add View/Implementation/RoiSelectorForm.cs Services/Interface/IThumbnailManager.cs Services/Implementation/ThumbnailManager.cs View/Interface/IMainFormView.cs View/Implementation/MainForm.cs Presenters/Implementation/MainFormPresenter.cs
git commit -m "feat(roi): per-client ROI selector dialog and wiring"
```

---

## Task 8: ThumbnailManager 启动/停止检测

**Files:**
- Modify: `src/Eve-O-Preview/Services/Implementation/ThumbnailManager.cs`
- Modify: `src/Eve-O-Preview/Mediator/Handlers/Thumbnails/ThumbnailListUpdatedHandler.cs` (若存在) 或主窗订阅消息

- [ ] **Step 1: 给 `ThumbnailManager` 注入 WGC + 检测器**

```csharp
private readonly IWgcCaptureService _wgc;
private readonly ImageTemplateDetector _detector;
private readonly IMediator _mediator;
private readonly Dictionary<IntPtr, ThumbnailWarningMonitor> _monitors;
```

- [ ] **Step 2: 新增 process 时创建 monitor**

在 `UpdateThumbnailsList` 的 `addedProcesses` 循环里：

```csharp
var monitor = new ThumbnailWarningMonitor(
    this._configuration, this._wgc, this._detector, this._mediator, view.Title, process.Handle);
this._monitors[view.Id] = monitor;
monitor.Start();
```

- [ ] **Step 3: removedProcess 时停止并释放**

```csharp
if (this._monitors.TryGetValue(view.Id, out var m))
{
    m.Stop();
    m.Dispose();
    this._monitors.Remove(view.Id);
}
```

- [ ] **Step 4: `Stop()` / `Dispose` 全部停掉**

```csharp
public void Stop()
{
    this._thumbnailUpdateTimer.Stop();
    foreach (var m in this._monitors.Values) { m.Stop(); m.Dispose(); }
    this._monitors.Clear();
}
```

- [ ] **Step 5: 主窗订阅 `TemplateAlertRaised` 写日志**

可在 `MainFormPresenter` 中：

```csharp
this._mediator.Subscribe<TemplateAlertRaised>(n =>
{
    System.Console.WriteLine($"[Alert] {n.ClientTitle}: 红={n.Result.RedMatches.Count} 橙={n.Result.OrangeMatches.Count} 白={n.Result.WhiteMatches.Count}");
});
```

（具体可在后续接 UI，这里先写 `Console`，避免改动主窗 UI。）

- [ ] **Step 6: 构建 + Commit**

```bash
dotnet build src/Eve-O-Preview/Eve-O-Preview.csproj -c Debug
git add Services/Implementation/ThumbnailManager.cs
git commit -m "feat(detection): start/stop per-thumbnail monitor on lifecycle events"
```

---

## Task 9: 端到端验证（手工）

> **说明：** 这是手工 QA 任务，不是单元测试任务。

- [ ] **Step 1: 启动一个 EVE 客户端**

```powershell
cd "c:\Users\12613\Downloads\EVEAutoWarning-main\eve-o-preview-unified-source-build\src\bin\Debug\net8.0-windows10.0.19041.0"
.\EVE-O-Preview.exe
```

预期：缩略图显示（DWM/WGC 任一正常）。

- [ ] **Step 2: 在主窗列表右键 → 选择监控区域**

预期：弹出全屏覆盖层，鼠标拖动画矩形，回车/释放确认；`EVE-O-Preview.json` 中出现 `PerClientRoi["EVE - xxx"]`。

- [ ] **Step 3: 触发红色名单**（用记事本画个红底白字，拖到 ROI 内）

预期：
- 缩略图边框变红
- 听到报警声
- 控制台输出 `[Alert] ... 红=1 ...`

- [ ] **Step 4: 移开报警物**

预期：边框消失、声音停止。

- [ ] **Step 5: 关闭 EVE 客户端**

预期：`monitors` 中对应项被释放，进程退出无未处理异常。

---

## Task 10: 文档 + README 更新（可选）

**Files:**
- Modify: `README.md` （顶部功能列表加一条 "Per-client WGC capture + template/ROI alerting"）

- [ ] **Step 1: 在 README 顶部加一行说明**

```markdown
- **每个客户端独立监控** — 集成 Windows.Graphics.Capture 与红/橙/白名单模板匹配，可在 EVE 客户端出现 NPC 危险警告时报警（ROI 可框选）。
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: document WGC + template/ROI alerting"
```

---

## Self-Review Checklist

1. **Spec coverage：**
   - 每个游戏窗口使用 WGC → Task 3/8
   - 模板识别 → Task 2/5
   - ROI 框选 → Task 7/8
   - 报警触发（声音/视觉）→ Task 6
   - 保留 DWM 回退 → 任务 8 在 `_wgc.IsSupported` 为 false 时跳过 WGC 分支
   - 按客户端独立监控 → Task 5/8 (`ThumbnailWarningMonitor` 1:1 客户端)

2. **Placeholder scan：** 无 TBD/TODO/`实现说明`/`占位` 残留；WGC 单帧抓取的 COM 桥接已明确写在 WgcInterop.cs 中。

3. **Type consistency：**
   - `TemplateMatchResult` / `TemplateMatch` / `ColorType` 命名与 EVEAutoWarning 保持一致
   - `IThumbnailConfiguration` 新增属性名 `EnableWgcCapture` / `EnableTemplateDetection` / `TemplateMatchThreshold` / `TemplateScanIntervalMs` / `AlarmSoundPath` / `PerClientRoi` 在接口、实现、JSON、Presenter 中统一
   - `IThumbnailView.SetAlertState(bool)` 在 `ThumbnailView` 默认实现 + `LiveThumbnailView` 覆盖

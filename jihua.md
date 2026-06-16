我仔细看了你的抓帧链路。核心判断：

**你现在最大的问题不是模板匹配，而是“WGC 抓帧服务的整体设计不稳定”：你把 WGC 这种持续会话型 API 写成了一个同步单帧** **`TryCaptureFrame(hwnd, out Bitmap)`** **服务，而且内部只维护一个** **`_currentHwnd/_session/_latestFrame`。这会导致 COM/WinRT 转换错误、多个窗口互相抢 session、第一帧等待不稳定、返回旧帧、资源释放混乱。**

另外，我这边 GitHub `main` 上看到的 `WgcCaptureService.cs` 还是旧版本：`RoGetActivationFactory` 仍然用 `[MarshalAs(UnmanagedType.HString)] string`，`CreateForWindow` 仍然是 `out object`，`_winrtDevice` 仍然用 `Marshal.GetObjectForIUnknown`。这些和你本地日志里的改动不完全一致，说明你本地可能又改了但还没推，或者我读到的是 main 上旧提交。下面我按仓库代码 + 你日志综合分析。

***

# 1. 你的 `WgcCaptureService` 抽象不对

你现在的接口本质是：

```
bool TryCaptureFrame(IntPtr hwnd, out Bitmap frame);

```

仓库里的接口也是这个意思：给定 HWND，输出 Bitmap。

但 WGC 不是这种“每次调用截一张图”的 API。WGC 的真实模型是：

```
Create session
StartCapture
FrameArrived event 持续到达
从最新帧缓存里取图
Stop session

```

也就是说，WGC 应该是：

```
IWindowCaptureSession CreateSession(IntPtr hwnd);

```

而不是：

```
TryCaptureFrame(hwnd, out Bitmap)

```

你现在用 `TryCaptureFrame` 包了一层 session 生命周期，导致它每次既像“启动会话”，又像“拿一帧”，职责混在一起。你的 `TryCaptureFrame` 里如果 hwnd 变了就 `StopCapture()` 再 `StartCapture(hwnd)`，然后等 `_frameReady` 最多 500ms。

这就是后面一堆问题的源头。

***

# 2. 你现在只能稳定支持一个窗口，多窗口会互相打架

`WgcCaptureService` 内部只有一套状态：

```
private Direct3D11CaptureFramePool _framePool;
private GraphicsCaptureSession _session;
private GraphicsCaptureItem _captureItem;
private IntPtr _currentHwnd;
private Bitmap _latestFrame;
private volatile bool _frameReady;

```

但 `ThumbnailManager` 是给每个 EVE thumbnail 都创建一个 `ThumbnailWarningMonitor`，并且传进去的是同一个 `_wgc` 服务实例：

```
var monitor = new ThumbnailWarningMonitor(
    _configuration, _wgc, _detector, _mediator,
    kv.Value.Title, kv.Key);

```

这意味着如果你有多个 EVE 窗口：

```
Monitor A 调 TryCaptureFrame(hwndA)
  -> _currentHwnd = hwndA
  -> sessionA 开始

Monitor B 调 TryCaptureFrame(hwndB)
  -> hwnd 不同
  -> StopCapture()
  -> sessionA 被关
  -> sessionB 开始

Monitor A 下一轮又进来
  -> hwnd 不同
  -> StopCapture()
  -> sessionB 被关
  -> sessionA 重启

```

结果就是：

```
多个窗口互相 StopCapture / StartCapture
WGC session 永远不稳定
FrameArrived 还没来就被另一个窗口停掉

```

所以你的捕获服务不能做成一个全局 `_currentHwnd`。正确结构应该是：

```
IWindowCaptureService
  CreateSession(hwnd)

WindowCaptureSession(hwnd)
  自己持有自己的 framePool/session/latestFrame

```

每个窗口一个 session，而不是一个服务全局切换 HWND。

***

# 3. `TryCaptureFrame` 会返回旧帧

你现在逻辑是：

```
while (!_frameReady && waited < 500)
{
    Thread.Sleep(5);
    waited += 5;
}

lock (_lock)
{
    if (_latestFrame != null)
    {
        frame = (Bitmap)_latestFrame.Clone();
        _frameReady = false;
        return true;
    }
}

```

问题是：你等的是 `_frameReady`，但最后判断的是 `_latestFrame != null`。

这会导致：

```
第一次成功拿到一帧
_frameReady = true
返回时你设置 _frameReady = false
_latestFrame 仍然保留

下一次如果没有新帧到达：
等 500ms
_frameReady 仍然 false
但 _latestFrame != null
于是返回旧帧

```

所以后面模板检测可能一直在处理旧图。

正确做法之一是加帧序号：

```
private long _frameVersion;
private long _lastReturnedVersion;

```

只有 `_frameVersion > _lastReturnedVersion` 才返回新帧。

或者更简单：不要把它设计成同步 `TryCaptureFrame`，改成事件式：

```
session.FrameArrived += frame => detectionPipeline.Process(frame);

```

***

# 4. COM / WinRT interop 混用了三套模型

你的代码里混用了：

```
SharpDX COM 对象
Marshal.GetObjectForIUnknown
CsWinRT projected Windows.Graphics.* 类型
手写 COM interop 接口

```

这很容易炸。

当前仓库里的几个明确问题：

## 4.1 `RoGetActivationFactory` 签名错

仓库 main 里还是：

```
[DllImport("api-ms-win-core-winrt-l1-1-0.dll", PreserveSig = false)]
private static extern int RoGetActivationFactory(
    [MarshalAs(UnmanagedType.HString)] string activatableClassId,
    [In] ref Guid iid,
    [MarshalAs(UnmanagedType.IUnknown)] out object factory);

```

你之前已经遇到过：

```
Cannot marshal 'parameter #1'

```

原因就是这里。正确方向是：

```
WindowsCreateString -> RoGetActivationFactory(IntPtr hstring, ...) -> WindowsDeleteString

```

不要直接 `[MarshalAs(UnmanagedType.HString)] string`。

***

## 4.2 `CreateForWindow` 签名错

仓库 main 里还是：

```
void CreateForWindow(
    [In] IntPtr window,
    [In] ref Guid iid,
    [MarshalAs(UnmanagedType.IUnknown)] out object result);

```

你后面本地改成 `out IntPtr` 仍然报错。比较稳的签名应该是：

```
[PreserveSig]
int CreateForWindow(
    IntPtr window,
    ref Guid iid,
    out IntPtr result);

```

并且不要再用：

```
typeof(GraphicsCaptureItem).GUID

```

仓库 main 里现在还在用它。

应该用 `IGraphicsCaptureItem` 的 IID：

```
private static readonly Guid IID_IGraphicsCaptureItem =
    new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

```

然后通过 CsWinRT `FromAbi` 转成 `GraphicsCaptureItem`。

***

## 4.3 `_winrtDevice` 转换错

仓库 main 里是：

```
_winrtDevice = (IDirect3DDevice)Marshal.GetObjectForIUnknown(winrtDevicePtr);
Marshal.Release(winrtDevicePtr);

```

你最新日志已经证明这里会在后面炸：

```
Failed to create a CCW for object of type 'System.__ComObject'
for interface IID 'A37624AB-8D5F-4650-9D3E-9EAE3D9BC670'

```

也就是 `Direct3D11CaptureFramePool.Create` 不接受这个普通 `System.__ComObject`。它需要的是 CsWinRT 能识别的 `IDirect3DDevice` 投影对象。

这里应该用：

```
_winrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(winrtDevicePtr);

```

而不是：

```
Marshal.GetObjectForIUnknown(...)

```

同时 `StopCapture()` 里也不能再：

```
Marshal.ReleaseComObject(_winrtDevice);

```

仓库现在还这么做。

用 `FromAbi` 得到的 WinRT projection，直接置空即可。

***

# 5. D3D device 生命周期不对

你在 `StartCapture` 里局部创建：

```
var d3dDevice = new D3DDevice(
    SharpDX.Direct3D.DriverType.Hardware,
    DeviceCreationFlags.BgraSupport);

```

但成功路径里没有保存它，也没有释放它。失败路径部分地方会 `d3dDevice.Dispose()`，成功路径没有。

这会有两个问题：

```
1. 资源泄漏
2. WGC frame pool / surface 后续依赖的底层 D3D device 生命周期不清晰

```

应该变成字段：

```
private D3DDevice _d3dDevice;

```

启动时：

```
_d3dDevice = new D3DDevice(...);

```

停止时：

```
_d3dDevice?.Dispose();
_d3dDevice = null;

```

***

# 6. `SurfaceToBitmap` 也还有隐患

你现在从 WinRT surface 拿 D3D11 texture：

```
var dxgiAccess = (IDirect3DDxgiInterfaceAccess)surface;
dxgiAccess.GetInterface(ref iid, out var texturePtr);

```

这和前面的 `_winrtDevice` 问题类似：`surface` 是 WinRT projected object，不一定能直接 C# cast 成你手写的 COM interface。后面很可能还会出现类似的 `InvalidCastException`。

更稳的做法还是通过 CsWinRT ABI/Interop 拿 interface，不要直接：

```
(IDirect3DDxgiInterfaceAccess)surface

```

另外，你这里拿到 `texturePtr` 后：

```
using (var texture = new SharpDX.Direct3D11.Texture2D(texturePtr))

```

但没有清晰处理 `texturePtr` 的 COM 引用计数。至少要保证用完后 `Marshal.Release(texturePtr)`，否则可能泄漏。

***

# 7. `SurfaceToBitmap` 每帧 GPU→CPU 拷贝，性能会比较重

你每一帧都：

```
GPU texture
  -> staging texture
  -> MapSubresource
  -> new Bitmap
  -> MemoryCopy

```

对应代码在这里。

这能跑，但后面如果多窗口 + 500ms 扫描 + OCR，会越来越重。

优化方向：

```
第一版可以接受
但后续应该：
1. 只裁 ROI 再转 CPU，而不是整张 1920x1009 转 Bitmap
2. 或降低抓帧频率
3. 或把模板匹配放到 GPU / OpenCV Mat 流程
4. 不要每帧 new Bitmap，至少复用缓冲

```

现在先不用优化，但要知道这个不是长远设计。

***

# 8. `ThumbnailWarningMonitor` 的线程模型不适合 WGC

你现在 monitor 是：

```
_cts = new CancellationTokenSource();
_ = Task.Run(() => Loop(_cts.Token));

```

也就是 WGC session 是在 ThreadPool 后台线程里创建的。WGC/WinRT/D3D 这套东西放后台线程不是绝对不能跑，但更容易出现：

```
COM apartment 不一致
FrameArrived 不触发
WinRT 对象 marshal 异常
session 生命周期和线程绑定混乱

```

更稳的设计是：

```
WGC session 创建/销毁在 UI STA 线程或专用 STA 线程
FrameArrived 回调里只缓存帧
检测/OCR 在后台线程消费缓存帧

```

不要在后台检测循环里直接创建 WGC session。

***

# 9. `TryCaptureFrame` 吞异常太多

仓库 main 里 `TryCaptureFrame` 还有：

```
catch
{
    return false;
}

```

`StartCapture` 里也只是：

```
System.Diagnostics.Debug.WriteLine(...)

```

`OnFrameArrived` 和 `SurfaceToBitmap` 也是 `Debug.WriteLine`。

这会导致日志里只看到：

```
TryCaptureFrame 返回 false

```

但不知道哪一层错了。

你后面加了 DetectionLog 才定位到问题。这个应该保留，不要再退回 `Debug.WriteLine`。

***

# 10. `Detection` 逻辑和 `Capture` 强耦合

`ThumbnailWarningMonitor` 里现在同时做：

```
抓帧
ROI
保存 debug 图
模板匹配
OCR
状态机
MediatR 发布

```

对应代码在一个 loop 里直接串起来。

这后面会越来越难调。

建议拆成：

```
WindowCaptureSession
  只负责 WGC 帧

DetectionPipeline
  只负责 ROI + 模板 + OCR

AlertStateMachine
  只负责连续帧确认 / 解除

ThumbnailWarningMonitor
  只负责把这三者组装起来

```

***

# 11. 当前项目结构里还有命名/文件不一致

仓库里接口文件叫：

```
IWgcCaptureService.cs

```

但里面实际接口是：

```
public interface IWindowCaptureService

```

这不会导致运行错误，但会误导你排查。建议改名：

```
IWindowCaptureService.cs

```

然后实现类分清：

```
WgcCaptureService
PrintWindowCaptureService
DesktopDuplicationCaptureService

```

***

# 12. LightInject 注册注释和真实生命周期不完全一致

`Program.cs` 注释说：

```
// Singleton registration is used for services

```

但实际显式注册：

```
container.Register<IWindowCaptureService, WgcCaptureService>();

```

而 `LightInjectContainer.Register<TService,TImplementation>()` 只是：

```
this._container.Register<TService, TImplementation>();

```

没有指定 `PerContainerLifetime`。

这不一定是当前 bug，因为 `ThumbnailManager` 自己通常是 per-container，被构造时注入了一份服务。但注释和真实行为不一致。后续如果你在别处再 resolve `IWindowCaptureService`，可能不是同一个实例。

更推荐：

```
container.RegisterInstance<IWindowCaptureService>(new WgcCaptureService());

```

或者改容器封装，支持明确生命周期。

***

# 你现在应该怎么改：不要继续补丁式修 COM

你现在的最大风险是继续一层层补：

```
RoGetActivationFactory 修了
CreateForWindow 又炸
IDirect3DDevice 又炸
SurfaceToBitmap 还会炸
线程再炸
多窗口再炸

```

建议你重新整理抓帧层。

***

## 正确结构

### 接口

```
public interface IWindowCaptureService
{
    IWindowCaptureSession CreateSession(IntPtr hwnd);
}

```

```
public interface IWindowCaptureSession : IDisposable
{
    IntPtr WindowHandle { get; }

    event Action<Bitmap> FrameArrived;

    void Start();
    void Stop();

    bool TryGetLatestFrame(out Bitmap frame);
}

```

***

## 每个窗口一个 session

不要全局 `_currentHwnd`。

```
public sealed class WgcCaptureSession : IWindowCaptureSession
{
    private readonly IntPtr _hwnd;

    private D3DDevice _d3dDevice;
    private IDirect3DDevice _winrtDevice;
    private GraphicsCaptureItem _item;
    private Direct3D11CaptureFramePool _framePool;
    private GraphicsCaptureSession _session;

    private Bitmap _latestFrame;
    private long _frameVersion;
}

```

`WgcCaptureService` 只做工厂：

```
public sealed class WgcCaptureService : IWindowCaptureService
{
    public IWindowCaptureSession CreateSession(IntPtr hwnd)
    {
        return new WgcCaptureSession(hwnd);
    }
}

```

`ThumbnailWarningMonitor` 持有自己的 session：

```
private IWindowCaptureSession _session;

```

启动时：

```
_session = _captureService.CreateSession(_hwnd);
_session.Start();

```

循环里：

```
if (_session.TryGetLatestFrame(out var bmp))
{
    // ROI + detect
}

```

停止时：

```
_session?.Dispose();

```

这样多窗口不会互相 Stop/Start。

***

# 最小修复优先级

你现在不用一次重构完，但至少按这个优先级做。

## 第一优先级：修 WGC interop

必须改：

```
RoGetActivationFactory HSTRING
CreateForWindow PreserveSig + out IntPtr
IGraphicsCaptureItem IID
IDirect3DDevice FromAbi
不要 ReleaseComObject(_winrtDevice)

```

现在仓库 main 这些都还没完全改。

## 第二优先级：每窗口 session

否则多开 EVE 时一定互相抢。

## 第三优先级：修 `TryCaptureFrame` 旧帧问题

不要 `_frameReady=false` 后还返回 `_latestFrame`。

## 第四优先级：SurfaceToBitmap

等 FramePool 能创建成功后，下一层大概率会卡这里。把日志打全。

## 第五优先级：模板匹配

抓帧成功、debug 图正确之后，再调阈值和模板。

***

# 一句话总结

你整个获取过程的问题不是单个 API 调错，而是**架构层把 WGC 当成同步截图函数用了**。WGC 应该是“每个窗口一个持续捕获 session”，而你现在是“一个全局服务按 hwnd 来回切换 session 并同步等帧”。在这个基础上，你还混用了 `Marshal.GetObjectForIUnknown`、CsWinRT projected type、手写 COM interface，所以才会一层层出现 `HSTRING`、`CreateForWindow`、`IDirect3DDevice`、`FramePool.Create` 的异常。

先把抓帧层重构成：

```
IWindowCaptureService
  -> CreateSession(hwnd)
      -> WgcCaptureSession
          -> StartCapture
          -> FrameArrived
          -> TryGetLatestFrame

```

然后模板匹配层再接这个 session 的最新帧。

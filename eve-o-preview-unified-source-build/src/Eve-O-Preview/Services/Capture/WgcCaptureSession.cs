using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using EveOPreview.Services.Detection;
using SharpDX.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using D3DDevice = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace EveOPreview.Services.Capture
{
    /// <summary>
    /// 单个窗口的 WGC 持续捕获会话。
    /// 每个窗口一个独立 session，自己持有 D3D device / framePool / session / latestFrame，
    /// 避免多窗口互相 Stop/Start。
    /// </summary>
    public sealed class WgcCaptureSession : IWindowCaptureSession
    {
        #region WinRT Interop

        [DllImport("d3d11.dll")]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(
            IntPtr dxgiDevice,
            out IntPtr graphicsDevice);

        [DllImport("combase.dll", PreserveSig = true)]
        private static extern int WindowsCreateString(
            [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
            int length,
            out IntPtr hstring);

        [DllImport("combase.dll", PreserveSig = true)]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("api-ms-win-core-winrt-l1-1-0.dll", PreserveSig = true)]
        private static extern int RoGetActivationFactory(
            IntPtr activatableClassId,
            [In] ref Guid iid,
            [MarshalAs(UnmanagedType.IUnknown)] out object factory);

        /// <summary>IGraphicsCaptureItemInterop</summary>
        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            [PreserveSig]
            int CreateForWindow(
                [In] IntPtr window,
                [In] ref Guid iid,
                [Out] out IntPtr result);

            [PreserveSig]
            int CreateForMonitor(
                [In] IntPtr monitor,
                [In] ref Guid iid,
                [Out] out IntPtr result);
        }

        /// <summary>IDirect3DDxgiInterfaceAccess</summary>
        [ComImport]
        [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDirect3DDxgiInterfaceAccess
        {
            void GetInterface([In] ref Guid iid, [Out] out IntPtr pObject);
        }

        // ID3D11Texture2D GUID
        private static readonly Guid IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        // IGraphicsCaptureItem GUID
        private static readonly Guid IID_IGraphicsCaptureItem =
            new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        #endregion

        private readonly IntPtr _hwnd;
        private readonly object _lock = new();

        // WGC 资源（每个 session 独立）
        private D3DDevice _d3dDevice;
        private IDirect3DDevice _winrtDevice;
        private GraphicsCaptureItem _captureItem;
        private Direct3D11CaptureFramePool _framePool;
        private GraphicsCaptureSession _session;

        // 帧缓存 + 版本号（避免返回旧帧）
        private Bitmap _latestFrame;
        private long _frameVersion;
        private long _lastReturnedVersion;

        private bool _isRunning;
        private bool _disposed;

        public WgcCaptureSession(IntPtr hwnd)
        {
            _hwnd = hwnd;
        }

        public IntPtr WindowHandle => _hwnd;

        public bool IsRunning
        {
            get
            {
                lock (_lock) { return _isRunning; }
            }
        }

        public bool IsSupported
        {
            get
            {
                try { return GraphicsCaptureSession.IsSupported(); }
                catch (Exception ex) { DetectionLog.Write($"[WGC] IsSupported异常: {ex}"); return false; }
            }
        }

        public event Action<Bitmap> FrameArrived;

        public void Start()
        {
            lock (_lock)
            {
                if (_isRunning) return;
                if (_hwnd == IntPtr.Zero)
                {
                    DetectionLog.Write("[WGC] Start 失败: hwnd 为 Zero");
                    return;
                }
                if (!IsSupported)
                {
                    DetectionLog.Write("[WGC] Start 失败: 平台不支持 WGC");
                    return;
                }

                if (!StartCapture())
                {
                    DetectionLog.Write("[WGC] Start 失败: StartCapture 返回 false");
                    return;
                }
                _isRunning = true;
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isRunning && _framePool == null && _session == null) return;
                _isRunning = false;
                StopCapture();
            }
        }

        /// <summary>
        /// 仅当有新帧（_frameVersion > _lastReturnedVersion）时返回 true。
        /// 避免旧 _frameReady=false 后仍返回 _latestFrame 的问题。
        /// </summary>
        public bool TryGetLatestFrame(out Bitmap frame)
        {
            frame = null;
            lock (_lock)
            {
                if (_latestFrame == null) return false;
                if (_frameVersion <= _lastReturnedVersion) return false;

                frame = (Bitmap)_latestFrame.Clone();
                _lastReturnedVersion = _frameVersion;
                return true;
            }
        }

        private bool StartCapture()
        {
            try
            {
                // 1. 创建 SharpDX D3D11 设备（保存为字段，统一释放）
                _d3dDevice = new D3DDevice(
                    SharpDX.Direct3D.DriverType.Hardware,
                    DeviceCreationFlags.BgraSupport);

                IntPtr winrtDevicePtr = IntPtr.Zero;
                using (var dxgiDevice = _d3dDevice.QueryInterface<SharpDX.DXGI.Device>())
                {
                    int hr = CreateDirect3D11DeviceFromDXGIDevice(
                        dxgiDevice.NativePointer,
                        out winrtDevicePtr);

                    if (hr < 0 || winrtDevicePtr == IntPtr.Zero)
                    {
                        DetectionLog.Write($"[WGC] CreateDirect3D11DeviceFromDXGIDevice failed: hr=0x{hr:X8}");
                        _d3dDevice.Dispose();
                        _d3dDevice = null;
                        return false;
                    }
                }

                // 关键修复：用 CsWinRT MarshalInterface.FromAbi 而不是 Marshal.GetObjectForIUnknown。
                // GetObjectForIUnknown 得到的是普通 System.__ComObject，FramePool.Create 会拒绝。
                try
                {
                    _winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(winrtDevicePtr);
                }
                finally
                {
                    // FromAbi 会在内部增加引用，这里释放原始指针的引用
                    Marshal.Release(winrtDevicePtr);
                }

                if (_winrtDevice == null)
                {
                    DetectionLog.Write("[WGC] IDirect3DDevice FromAbi 返回 null");
                    _d3dDevice.Dispose();
                    _d3dDevice = null;
                    return false;
                }

                // 2. 通过 COM interop 创建 GraphicsCaptureItem
                var interopIid = typeof(IGraphicsCaptureItemInterop).GUID;
                var className = "Windows.Graphics.Capture.GraphicsCaptureItem";
                WindowsCreateString(className, className.Length, out var hstring);
                object factoryObj;
                try
                {
                    RoGetActivationFactory(hstring, ref interopIid, out factoryObj);
                }
                finally
                {
                    WindowsDeleteString(hstring);
                }
                var interop = (IGraphicsCaptureItemInterop)factoryObj;

                var captureItemIid = IID_IGraphicsCaptureItem;
                IntPtr captureItemPtr = IntPtr.Zero;

                int createHr = interop.CreateForWindow(_hwnd, ref captureItemIid, out captureItemPtr);

                if (createHr < 0 || captureItemPtr == IntPtr.Zero)
                {
                    DetectionLog.Write($"[WGC] CreateForWindow failed: hr=0x{createHr:X8}, ptr={captureItemPtr}");
                    StopCapture();
                    return false;
                }

                try
                {
                    _captureItem = MarshalInterface<GraphicsCaptureItem>.FromAbi(captureItemPtr);

                    if (_captureItem == null)
                    {
                        DetectionLog.Write("[WGC] CreateForWindow: FromAbi returned null");
                        StopCapture();
                        return false;
                    }

                    DetectionLog.Write($"[WGC] CreateForWindow OK: item size={_captureItem.Size.Width}x{_captureItem.Size.Height}");
                }
                finally
                {
                    Marshal.Release(captureItemPtr);
                }

                // 3. 创建帧池和会话
                var size = _captureItem.Size;
                _framePool = Direct3D11CaptureFramePool.Create(
                    _winrtDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    size);

                _session = _framePool.CreateCaptureSession(_captureItem);
                _session.IsCursorCaptureEnabled = false;
                _framePool.FrameArrived += OnFrameArrived;
                _session.StartCapture();

                DetectionLog.Write($"[WGC] Session 启动成功 hwnd={_hwnd}");
                return true;
            }
            catch (Exception ex)
            {
                DetectionLog.Write($"[WGC] StartCapture异常: {ex}");
                StopCapture();
                return false;
            }
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            try
            {
                using (var frame = sender.TryGetNextFrame())
                {
                    if (frame == null) return;

                    var surface = frame.Surface;
                    if (surface == null) return;

                    var bitmap = SurfaceToBitmap(surface, frame.ContentSize);
                    if (bitmap == null) return;

                    Bitmap oldFrame;
                    lock (_lock)
                    {
                        oldFrame = _latestFrame;
                        _latestFrame = bitmap;
                        _frameVersion++;
                    }
                    oldFrame?.Dispose();
                    FrameArrived?.Invoke(bitmap);
                }
            }
            catch (Exception ex)
            {
                DetectionLog.Write($"[WGC] OnFrameArrived异常: {ex}");
            }
        }

        private static unsafe Bitmap SurfaceToBitmap(IDirect3DSurface surface, Windows.Graphics.SizeInt32 size)
        {
            try
            {
                // 通过 IDirect3DDxgiInterfaceAccess 拿原生 D3D11 纹理
                var dxgiAccess = (IDirect3DDxgiInterfaceAccess)surface;
                var iid = IID_ID3D11Texture2D;
                dxgiAccess.GetInterface(ref iid, out var texturePtr);

                try
                {
                    using (var texture = new SharpDX.Direct3D11.Texture2D(texturePtr))
                    {
                        var desc = texture.Description;
                        desc.Usage = ResourceUsage.Staging;
                        desc.BindFlags = BindFlags.None;
                        desc.CpuAccessFlags = CpuAccessFlags.Read;
                        desc.OptionFlags = ResourceOptionFlags.None;

                        using (var staging = new SharpDX.Direct3D11.Texture2D(texture.Device, desc))
                        {
                            texture.Device.ImmediateContext.CopyResource(texture, staging);

                            var dataBox = texture.Device.ImmediateContext.MapSubresource(
                                staging, 0, MapMode.Read, MapFlags.None);

                            var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
                            var bmpData = bitmap.LockBits(
                                new Rectangle(0, 0, size.Width, size.Height),
                                ImageLockMode.WriteOnly,
                                PixelFormat.Format32bppArgb);

                            try
                            {
                                int srcStride = dataBox.RowPitch;
                                int dstStride = bmpData.Stride;
                                int copySize = Math.Min(srcStride, dstStride);

                                byte* src = (byte*)dataBox.DataPointer;
                                byte* dst = (byte*)bmpData.Scan0;

                                for (int row = 0; row < size.Height; row++)
                                {
                                    System.Buffer.MemoryCopy(src, dst, copySize, copySize);
                                    src += srcStride;
                                    dst += dstStride;
                                }
                            }
                            finally
                            {
                                bitmap.UnlockBits(bmpData);
                                texture.Device.ImmediateContext.UnmapSubresource(staging, 0);
                            }

                            return bitmap;
                        }
                    }
                }
                finally
                {
                    // 释放 GetInterface 返回的 COM 引用，避免泄漏
                    if (texturePtr != IntPtr.Zero)
                        Marshal.Release(texturePtr);
                }
            }
            catch (Exception ex)
            {
                DetectionLog.Write($"[WGC] SurfaceToBitmap异常: {ex}");
                return null;
            }
        }

        private void StopCapture()
        {
            if (_framePool != null)
            {
                try { _framePool.FrameArrived -= OnFrameArrived; } catch { }
                try { _framePool.Dispose(); } catch { }
                _framePool = null;
            }

            if (_session != null)
            {
                try { _session.Dispose(); } catch { }
                _session = null;
            }

            _captureItem = null;

            // 关键修复：FromAbi 得到的 WinRT projection 不要 ReleaseComObject，直接置空。
            // ReleaseComObject 会破坏 CsWinRT 的 RCW 管理，导致后续 CCW 错误。
            _winrtDevice = null;

            if (_d3dDevice != null)
            {
                try { _d3dDevice.Dispose(); } catch { }
                _d3dDevice = null;
            }

            Bitmap oldFrame;
            lock (_lock)
            {
                oldFrame = _latestFrame;
                _latestFrame = null;
                _frameVersion = 0;
                _lastReturnedVersion = 0;
            }
            oldFrame?.Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}

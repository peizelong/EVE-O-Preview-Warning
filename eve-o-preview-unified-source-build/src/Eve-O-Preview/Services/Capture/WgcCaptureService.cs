using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using D3DDevice = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace EveOPreview.Services.Capture
{
    /// <summary>
    /// 基于 Windows.Graphics.Capture (WGC) 的真正 GPU 抓帧服务。
    /// 适用于 DirectX 游戏窗口（如 EVE Online），能抓取 GPU 渲染的实际画面。
    /// </summary>
    public sealed class WgcCaptureService : IWindowCaptureService, IDisposable
    {
        #region WinRT Interop

        [DllImport("d3d11.dll")]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(
            IntPtr dxgiDevice,
            out IntPtr graphicsDevice);

        [DllImport("api-ms-win-core-winrt-l1-1-0.dll", PreserveSig = false)]
        private static extern int RoGetActivationFactory(
            [MarshalAs(UnmanagedType.HString)] string activatableClassId,
            [In] ref Guid iid,
            [MarshalAs(UnmanagedType.IUnknown)] out object factory);

        /// <summary>IGraphicsCaptureItemInterop</summary>
        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            void CreateForWindow(
                [In] IntPtr window,
                [In] ref Guid iid,
                [MarshalAs(UnmanagedType.IUnknown)] out object result);

            void CreateForMonitor(
                [In] IntPtr monitor,
                [In] ref Guid iid,
                [MarshalAs(UnmanagedType.IUnknown)] out object result);
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

        #endregion

        private IDirect3DDevice _winrtDevice;
        private Direct3D11CaptureFramePool _framePool;
        private GraphicsCaptureSession _session;
        private GraphicsCaptureItem _captureItem;
        private IntPtr _currentHwnd;
        private Bitmap _latestFrame;
        private readonly object _lock = new();
        private volatile bool _frameReady;
        private bool _disposed;

        public bool IsSupported
        {
            get
            {
                try { return GraphicsCaptureSession.IsSupported(); }
                catch { return false; }
            }
        }

        public bool TryCaptureFrame(IntPtr hwnd, out Bitmap frame)
        {
            frame = null;
            if (hwnd == IntPtr.Zero || !IsSupported) return false;

            try
            {
                if (_currentHwnd != hwnd)
                {
                    StopCapture();
                    if (!StartCapture(hwnd)) return false;
                }

                int waited = 0;
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
                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool StartCapture(IntPtr hwnd)
        {
            try
            {
                // 1. 创建 SharpDX D3D11 设备
                var d3dDevice = new D3DDevice(
                    SharpDX.Direct3D.DriverType.Hardware,
                    DeviceCreationFlags.BgraSupport);

                using (var dxgiDevice = d3dDevice.QueryInterface<SharpDX.DXGI.Device>())
                {
                    int hr = CreateDirect3D11DeviceFromDXGIDevice(
                        dxgiDevice.NativePointer,
                        out var winrtDevicePtr);

                    if (hr < 0 || winrtDevicePtr == IntPtr.Zero)
                    {
                        d3dDevice.Dispose();
                        return false;
                    }

                    _winrtDevice = (IDirect3DDevice)Marshal.GetObjectForIUnknown(winrtDevicePtr);
                    Marshal.Release(winrtDevicePtr);
                }

                if (_winrtDevice == null)
                {
                    d3dDevice.Dispose();
                    return false;
                }

                // 2. 通过 COM interop 创建 GraphicsCaptureItem
                var interopIid = typeof(IGraphicsCaptureItemInterop).GUID;
                RoGetActivationFactory(
                    "Windows.Graphics.Capture.GraphicsCaptureItem",
                    ref interopIid,
                    out var factoryObj);
                var interop = (IGraphicsCaptureItemInterop)factoryObj;

                var captureItemIid = typeof(GraphicsCaptureItem).GUID;
                interop.CreateForWindow(hwnd, ref captureItemIid, out var captureItemObj);
                _captureItem = (GraphicsCaptureItem)captureItemObj;

                if (_captureItem == null)
                {
                    StopCapture();
                    return false;
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

                _currentHwnd = hwnd;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WGC] StartCapture: {ex.Message}");
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

                    lock (_lock)
                    {
                        _latestFrame?.Dispose();
                        _latestFrame = bitmap;
                        _frameReady = true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WGC] OnFrameArrived: {ex.Message}");
            }
        }

        private static unsafe Bitmap SurfaceToBitmap(IDirect3DSurface surface, Windows.Graphics.SizeInt32 size)
        {
            try
            {
                // 获取原生 D3D11 纹理
                var dxgiAccess = (IDirect3DDxgiInterfaceAccess)surface;
                var iid = IID_ID3D11Texture2D;
                dxgiAccess.GetInterface(ref iid, out var texturePtr);

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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WGC] SurfaceToBitmap: {ex.Message}");
                return null;
            }
        }

        private void StopCapture()
        {
            _frameReady = false;
            _currentHwnd = IntPtr.Zero;

            if (_framePool != null)
            {
                _framePool.FrameArrived -= OnFrameArrived;
                _framePool.Dispose();
                _framePool = null;
            }

            _session?.Dispose();
            _session = null;
            _captureItem = null;

            if (_winrtDevice != null)
            {
                Marshal.ReleaseComObject(_winrtDevice);
                _winrtDevice = null;
            }

            lock (_lock)
            {
                _latestFrame?.Dispose();
                _latestFrame = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopCapture();
        }
    }
}
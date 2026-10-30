using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace ShotLite;

/// <summary>
/// Windows.Graphics.Capture frames cropped by Direct3D 11 into a CPU-readable texture, used from the
/// recording thread only. D3D is reached through raw vtable slots, so the app needs no wrapper library
/// for the handful of methods it uses.
/// </summary>
internal sealed unsafe class CaptureSource : IDisposable
{
    private const int D3D_DRIVER_TYPE_HARDWARE = 1;
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const uint D3D11_SDK_VERSION = 7;
    private const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    private const uint D3D11_USAGE_STAGING = 3;
    private const uint D3D11_CPU_ACCESS_READ = 0x20000;
    private const uint D3D11_MAP_READ = 1;

    // Vtable slots, in the order of the Windows SDK headers.
    private const int InteropCreateForMonitor = 4;
    private const int DeviceCreateTexture2D = 5;
    private const int ContextMap = 14;
    private const int ContextUnmap = 15;
    private const int ContextCopySubresourceRegion = 46;
    private const int DxgiAccessGetInterface = 3;

    private static readonly Guid ItemInteropIid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid ItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid DxgiDeviceIid = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
    private static readonly Guid DxgiAccessIid = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private static readonly Guid Texture2DIid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    [StructLayout(LayoutKind.Sequential)]
    private struct TextureDesc
    {
        public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Box
    {
        public uint Left, Top, Front, Right, Bottom, Back;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MappedSubresource
    {
        public IntPtr Data;
        public uint RowPitch, DepthPitch;
    }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels,
        uint featureLevelCount, uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr context);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    private readonly IntPtr _device, _context, _staging;
    private readonly Rectangle _crop;
    private readonly GraphicsCaptureItem _item;
    private readonly IDirect3DDevice _winrtDevice;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private global::Windows.Graphics.SizeInt32 _poolSize;
    private volatile bool _closed;
    private bool _mapped;

    /// <param name="crop">The recorded area in the item's own pixels; must fit inside it.</param>
    public CaptureSource(GraphicsCaptureItem item, Rectangle crop, bool cursor)
    {
        _item = item;
        _crop = crop;
        _item.Closed += (_, _) => _closed = true;

        Check(D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0,
            D3D11_SDK_VERSION, out _device, out _, out _context));

        var desc = new TextureDesc
        {
            Width = (uint)crop.Width,
            Height = (uint)crop.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleCount = 1,
            Usage = D3D11_USAGE_STAGING,
            CpuAccessFlags = D3D11_CPU_ACCESS_READ,
        };
        IntPtr staging;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, TextureDesc*, IntPtr, IntPtr*, int>)Slot(_device, DeviceCreateTexture2D))(
            _device, &desc, IntPtr.Zero, &staging));
        _staging = staging;

        _winrtDevice = CreateWinRtDevice(_device);
        _poolSize = item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
        _session = _pool.CreateCaptureSession(item);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            _session.IsCursorCaptureEnabled = cursor;
        RemoveCaptureBorder(_session);
    }

    /// <summary>The system closed the item, e.g. the monitor was unplugged.</summary>
    public bool IsClosed => _closed;

    public void Start() => _session.StartCapture();

    /// <summary>Takes the newest frame, if there is one, into the staging texture.</summary>
    /// <returns>True when a new frame was copied; otherwise the texture keeps the previous one.</returns>
    public bool TryCopyLatest()
    {
        Direct3D11CaptureFrame? latest = null;
        while (_pool.TryGetNextFrame() is { } frame)
        {
            latest?.Dispose();
            latest = frame;
        }
        if (latest == null)
            return false;

        using (latest)
        {
            var size = latest.ContentSize;
            if (size.Width != _poolSize.Width || size.Height != _poolSize.Height)
            {
                _poolSize = size;
                _pool.Recreate(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            }
            // The monitor's resolution changed below the recorded area: keep the last picture.
            if (_crop.Right > size.Width || _crop.Bottom > size.Height)
                return false;

            using var surface = latest.Surface;
            var texture = TextureOf(surface);
            try
            {
                var box = new Box
                {
                    Left = (uint)_crop.Left,
                    Top = (uint)_crop.Top,
                    Right = (uint)_crop.Right,
                    Bottom = (uint)_crop.Bottom,
                    Back = 1,
                };
                ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, Box*, void>)Slot(_context, ContextCopySubresourceRegion))(
                    _context, _staging, 0, 0, 0, 0, texture, 0, &box);
            }
            finally
            {
                Marshal.Release(texture);
            }
        }
        return true;
    }

    /// <summary>Maps the staging texture. Pair with <see cref="UnmapPixels"/> before the next copy.</summary>
    public IntPtr MapPixels(out int stride)
    {
        MappedSubresource mapped;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, MappedSubresource*, int>)Slot(_context, ContextMap))(
            _context, _staging, 0, D3D11_MAP_READ, 0, &mapped));
        _mapped = true;
        stride = (int)mapped.RowPitch;
        return mapped.Data;
    }

    public void UnmapPixels()
    {
        if (!_mapped)
            return;
        _mapped = false;
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)Slot(_context, ContextUnmap))(_context, _staging, 0);
    }

    public void Dispose()
    {
        UnmapPixels();
        _session.Dispose();
        _pool.Dispose();
        _winrtDevice.Dispose();
        Marshal.Release(_staging);
        Marshal.Release(_context);
        Marshal.Release(_device);
    }

    public static GraphicsCaptureItem ForMonitor(IntPtr monitor)
    {
        using var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        Check(Marshal.QueryInterface(factory.ThisPtr, in ItemInteropIid, out var interop));
        try
        {
            var itemIid = ItemIid;
            IntPtr abi;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)Slot(interop, InteropCreateForMonitor))(interop, monitor, &itemIid, &abi));
            try
            {
                return GraphicsCaptureItem.FromAbi(abi);
            }
            finally
            {
                Marshal.Release(abi);
            }
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    private static IDirect3DDevice CreateWinRtDevice(IntPtr device)
    {
        Check(Marshal.QueryInterface(device, in DxgiDeviceIid, out var dxgi));
        try
        {
            Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
            try
            {
                return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
            }
            finally
            {
                Marshal.Release(inspectable);
            }
        }
        finally
        {
            Marshal.Release(dxgi);
        }
    }

    private static IntPtr TextureOf(IDirect3DSurface surface)
    {
        var abi = MarshalInspectable<IDirect3DSurface>.FromManaged(surface);
        try
        {
            Check(Marshal.QueryInterface(abi, in DxgiAccessIid, out var access));
            try
            {
                var textureIid = Texture2DIid;
                IntPtr texture;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Slot(access, DxgiAccessGetInterface))(access, &textureIid, &texture));
                return texture;
            }
            finally
            {
                Marshal.Release(access);
            }
        }
        finally
        {
            Marshal.Release(abi);
        }
    }

    /// <summary>The yellow "being captured" frame can only be turned off on Windows 11; it's cosmetic, so failure is fine.</summary>
    private static void RemoveCaptureBorder(GraphicsCaptureSession session)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;
        try
        {
            GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless).AsTask().GetAwaiter().GetResult();
            session.IsBorderRequired = false;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
        }
    }

    private static void* Slot(IntPtr instance, int index) => (*(void***)instance)[index];

    private static void Check(int hresult) => Marshal.ThrowExceptionForHR(hresult);
}

using System.Runtime.InteropServices;

namespace ShotLite;

/// <summary>Receives top-down BGRA frames from the recording thread.</summary>
internal interface IFrameEncoder : IDisposable
{
    /// <param name="stride">Bytes per row of <paramref name="pixels"/>.</param>
    /// <param name="time">Time of the frame in 100 ns units from the start of the recording.</param>
    void Write(IntPtr pixels, int stride, long time);

    /// <param name="endTime">Length of the whole recording in 100 ns units.</param>
    void Finish(long endTime);
}

/// <summary>H.264 MP4 through the Media Foundation sink writer, so the app ships no codec of its own.</summary>
internal sealed unsafe class Mp4Encoder : IFrameEncoder
{
    public const int Fps = 30;

    private const uint MF_VERSION = 0x00020070;
    private const uint MFSTARTUP_LITE = 1;
    private const uint MFVideoInterlace_Progressive = 2;
    private const uint eAVEncH264VProfile_High = 100;
    private const int MinBitrate = 4_000_000, MaxBitrate = 40_000_000;
    private const double BitsPerPixel = 0.12;

    // Vtable slots, in the order of the Windows SDK headers.
    private const int AttributesSetUInt32 = 21;
    private const int AttributesSetUInt64 = 22;
    private const int AttributesSetGuid = 24;
    private const int SampleSetSampleTime = 36;
    private const int SampleSetSampleDuration = 38;
    private const int SampleAddBuffer = 42;
    private const int BufferLock = 3;
    private const int BufferUnlock = 4;
    private const int BufferSetCurrentLength = 6;
    private const int WriterAddStream = 3;
    private const int WriterSetInputMediaType = 4;
    private const int WriterBeginWriting = 5;
    private const int WriterWriteSample = 6;
    private const int WriterFinalize = 11;

    private static readonly Guid MajorType = new("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
    private static readonly Guid Subtype = new("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
    private static readonly Guid AvgBitrate = new("20332624-FB0D-4D9E-BD0D-CBF6786C102E");
    private static readonly Guid InterlaceMode = new("E2724BB8-E676-4806-B4B2-A8D6EFB44CCD");
    private static readonly Guid FrameSize = new("1652C33D-D6B2-4012-B834-72030849A37D");
    private static readonly Guid FrameRate = new("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");
    private static readonly Guid PixelAspectRatio = new("C6376A1E-8D0A-4027-BE45-6D9A0AD39BB6");
    private static readonly Guid DefaultStride = new("644B4E48-1E02-4516-B0EB-C01CA9D49AC6");
    private static readonly Guid Mpeg2Profile = new("AD76A80B-2D5C-4E0B-B375-64E520137036");
    private static readonly Guid EnableHardwareTransforms = new("A634A91C-822B-41B9-A494-4DE4643612B0");
    private static readonly Guid MediaTypeVideo = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid VideoFormatH264 = new("34363248-0000-0010-8000-00AA00389B71");
    private static readonly Guid VideoFormatRgb32 = new("00000016-0000-0010-8000-00AA00389B71");

    [DllImport("mfplat.dll")]
    private static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll")]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll")]
    private static extern int MFCreateAttributes(out IntPtr attributes, uint initialSize);

    [DllImport("mfplat.dll")]
    private static extern int MFCreateMediaType(out IntPtr mediaType);

    [DllImport("mfplat.dll")]
    private static extern int MFCreateSample(out IntPtr sample);

    [DllImport("mfplat.dll")]
    private static extern int MFCreateMemoryBuffer(uint maxLength, out IntPtr buffer);

    [DllImport("mfreadwrite.dll")]
    private static extern int MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr byteStream, IntPtr attributes, out IntPtr writer);

    private readonly int _width, _height;
    private readonly long _frameDuration = 10_000_000 / Fps;
    private IntPtr _writer;
    private uint _stream;
    private bool _started;

    /// <param name="width">Must be even, like <paramref name="height"/>: H.264 works on 2×2 chroma blocks.</param>
    public Mp4Encoder(string path, int width, int height)
    {
        _width = width;
        _height = height;
        try
        {
            Open(path, width, height);
        }
        catch
        {
            // No "using" protects a half-built object, and an open writer would keep the file locked.
            Dispose();
            throw;
        }
    }

    private void Open(string path, int width, int height)
    {
        Check(MFStartup(MF_VERSION, MFSTARTUP_LITE));
        _started = true;

        Check(MFCreateAttributes(out var attributes, 1));
        try
        {
            SetUInt32(attributes, EnableHardwareTransforms, 1);
            Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, attributes, out _writer));
        }
        finally
        {
            Marshal.Release(attributes);
        }

        Check(MFCreateMediaType(out var output));
        try
        {
            SetVideoFormat(output, VideoFormatH264);
            long bitrate = Math.Clamp((long)(width * height * Fps * BitsPerPixel), MinBitrate, MaxBitrate);
            SetUInt32(output, AvgBitrate, (uint)bitrate);
            SetUInt32(output, Mpeg2Profile, eAVEncH264VProfile_High);
            uint stream;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint*, int>)Slot(_writer, WriterAddStream))(_writer, output, &stream));
            _stream = stream;
        }
        finally
        {
            Marshal.Release(output);
        }

        Check(MFCreateMediaType(out var input));
        try
        {
            SetVideoFormat(input, VideoFormatRgb32);
            // A positive stride means top-down; without it RGB is assumed bottom-up and the video ends up flipped.
            SetUInt32(input, DefaultStride, (uint)(width * 4));
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)Slot(_writer, WriterSetInputMediaType))(_writer, _stream, input, IntPtr.Zero));
        }
        finally
        {
            Marshal.Release(input);
        }

        Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(_writer, WriterBeginWriting))(_writer));
    }

    public void Write(IntPtr pixels, int stride, long time)
    {
        int rowBytes = _width * 4;
        uint length = (uint)(rowBytes * _height);
        Check(MFCreateMemoryBuffer(length, out var buffer));
        try
        {
            byte* target;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, int>)Slot(buffer, BufferLock))(buffer, &target, null, null));
            var source = (byte*)pixels;
            for (int y = 0; y < _height; y++)
                Buffer.MemoryCopy(source + (long)y * stride, target + (long)y * rowBytes, rowBytes, rowBytes);
            Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(buffer, BufferUnlock))(buffer));
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, int>)Slot(buffer, BufferSetCurrentLength))(buffer, length));

            Check(MFCreateSample(out var sample));
            try
            {
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(sample, SampleAddBuffer))(sample, buffer));
                Check(((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Slot(sample, SampleSetSampleTime))(sample, time));
                Check(((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Slot(sample, SampleSetSampleDuration))(sample, _frameDuration));
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, int>)Slot(_writer, WriterWriteSample))(_writer, _stream, sample));
            }
            finally
            {
                Marshal.Release(sample);
            }
        }
        finally
        {
            Marshal.Release(buffer);
        }
    }

    public void Finish(long endTime) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(_writer, WriterFinalize))(_writer));

    public void Dispose()
    {
        if (_writer != IntPtr.Zero)
            Marshal.Release(_writer);
        _writer = IntPtr.Zero;
        if (_started)
            MFShutdown();
        _started = false;
    }

    private void SetVideoFormat(IntPtr mediaType, Guid format)
    {
        SetGuid(mediaType, MajorType, MediaTypeVideo);
        SetGuid(mediaType, Subtype, format);
        SetUInt32(mediaType, InterlaceMode, MFVideoInterlace_Progressive);
        SetRatio(mediaType, FrameSize, (uint)_width, (uint)_height);
        SetRatio(mediaType, FrameRate, Fps, 1);
        SetRatio(mediaType, PixelAspectRatio, 1, 1);
    }

    private static void SetUInt32(IntPtr attributes, Guid key, uint value) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, int>)Slot(attributes, AttributesSetUInt32))(attributes, &key, value));

    /// <summary>Media Foundation packs two 32-bit values into one 64-bit attribute.</summary>
    private static void SetRatio(IntPtr attributes, Guid key, uint high, uint low) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong, int>)Slot(attributes, AttributesSetUInt64))(attributes, &key, ((ulong)high << 32) | low));

    private static void SetGuid(IntPtr attributes, Guid key, Guid value) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Slot(attributes, AttributesSetGuid))(attributes, &key, &value));

    private static void* Slot(IntPtr instance, int index) => (*(void***)instance)[index];

    private static void Check(int hresult) => Marshal.ThrowExceptionForHR(hresult);
}

using System.Text;

namespace ShotLite;

/// <summary>
/// Animated GIF writer. Screen content changes little between frames, so a palette is kept while it still
/// fits and each frame stores only the changed rectangle; that gives stable colors and small files.
/// </summary>
internal sealed unsafe class GifEncoder : IFrameEncoder
{
    public const int Fps = 15;
    public const int MaxSeconds = 60;
    public const int MaxWidth = 800;

    private const int TicksPerCentisecond = 100_000;
    private const int MinLastFrameDelay = 10;
    private const byte TransparentIndex = 255;
    private const int PaletteSize = 255;
    private const int KeyCount = 1 << 15;
    /// <summary>Share of a frame's pixels (in 1/1000) that may use colors the palette wasn't built from.</summary>
    private const int MaxForeignPerMille = 15;

    private sealed record PendingFrame(Rectangle Bounds, byte[] Pixels, byte[] Palette, long Time);

    private sealed class ColorBox
    {
        public required int[] Keys;
        public int Population;

        public (int Span, int Shift) WidestChannel()
        {
            (int Span, int Shift) best = (-1, 0);
            foreach (int shift in new[] { 10, 5, 0 })
            {
                int min = 31, max = 0;
                foreach (int key in Keys)
                {
                    int value = (key >> shift) & 31;
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }
                if (max - min > best.Span)
                    best = (max - min, shift);
            }
            return best;
        }
    }

    private readonly Stream _output;
    private readonly int _width, _height;
    private readonly int[] _columns, _rows;
    private readonly ushort[] _keys;
    private readonly byte[] _indices;
    private readonly byte[] _previous;
    private readonly int[] _counts = new int[KeyCount];
    private readonly bool[] _inPalette = new bool[KeyCount];
    private readonly short[] _map = new short[KeyCount];
    private byte[] _palette = new byte[256 * 3];
    private int _paletteCount;
    private bool _hasPalette, _hasPrevious;
    private PendingFrame? _pending;
    private long _writtenCentiseconds;

    public GifEncoder(Stream output, int sourceWidth, int sourceHeight)
    {
        _output = output;
        _width = Math.Min(sourceWidth, MaxWidth);
        _height = Math.Max(1, (int)Math.Round((double)sourceHeight * _width / sourceWidth));

        _columns = Edges(sourceWidth, _width);
        _rows = Edges(sourceHeight, _height);
        _keys = new ushort[_width * _height];
        _indices = new byte[_width * _height];
        _previous = new byte[_width * _height];

        WriteHeader();
    }

    public void Write(IntPtr pixels, int stride, long time)
    {
        Downscale((byte*)pixels, stride);

        bool newPalette = EnsurePalette();
        for (int i = 0; i < _keys.Length; i++)
            _indices[i] = IndexOf(_keys[i]);

        Rectangle changed = newPalette || !_hasPrevious ? new Rectangle(0, 0, _width, _height) : ChangedBounds();
        if (changed.IsEmpty)
            return; // Identical frame: the pending one simply stays on screen longer.

        Flush(time);
        _pending = new PendingFrame(changed, ExtractPixels(changed, newPalette || !_hasPrevious), (byte[])_palette.Clone(), time);
        Array.Copy(_indices, _previous, _indices.Length);
        _hasPrevious = true;
    }

    public void Finish(long endTime)
    {
        if (_pending != null)
            Flush(Math.Max(endTime, _pending.Time + MinLastFrameDelay * (long)TicksPerCentisecond));
        _output.WriteByte(0x3B);
        _output.Flush();
    }

    public void Dispose() => _output.Dispose();

    // ---------------------------------------------------------------- frame preparation

    private static int[] Edges(int source, int target)
    {
        var edges = new int[target + 1];
        for (int i = 0; i <= target; i++)
            edges[i] = (int)((long)i * source / target);
        return edges;
    }

    /// <summary>Box filter into RGB555 keys; averaging keeps small UI text readable, unlike picking pixels.</summary>
    private void Downscale(byte* pixels, int stride)
    {
        fixed (ushort* keys = _keys)
        {
            for (int y = 0; y < _height; y++)
            {
                int top = _rows[y], bottom = Math.Max(_rows[y + 1], top + 1);
                ushort* target = keys + (long)y * _width;
                for (int x = 0; x < _width; x++)
                {
                    int left = _columns[x], right = Math.Max(_columns[x + 1], left + 1);
                    int b = 0, g = 0, r = 0;
                    for (int sy = top; sy < bottom; sy++)
                    {
                        byte* row = pixels + (long)sy * stride + left * 4L;
                        for (int sx = left; sx < right; sx++, row += 4)
                        {
                            b += row[0];
                            g += row[1];
                            r += row[2];
                        }
                    }
                    int count = (bottom - top) * (right - left);
                    target[x] = (ushort)(((r / count) >> 3 << 10) | ((g / count) >> 3 << 5) | ((b / count) >> 3));
                }
            }
        }
    }

    /// <returns>True when the palette was rebuilt, so the indices of the previous frame no longer compare.</returns>
    private bool EnsurePalette()
    {
        Array.Clear(_counts);
        foreach (ushort key in _keys)
            _counts[key]++;

        if (_hasPalette)
        {
            long foreign = 0;
            for (int key = 0; key < KeyCount; key++)
            {
                if (_counts[key] > 0 && !_inPalette[key])
                    foreign += _counts[key];
            }
            if (foreign * 1000 <= (long)_keys.Length * MaxForeignPerMille)
                return false;
        }

        BuildPalette();
        _hasPalette = true;
        return true;
    }

    /// <summary>Median cut over the colors of the current frame.</summary>
    private void BuildPalette()
    {
        var used = new List<int>();
        for (int key = 0; key < KeyCount; key++)
        {
            if (_counts[key] > 0)
                used.Add(key);
        }

        var boxes = new List<ColorBox> { new() { Keys = used.ToArray(), Population = _keys.Length } };
        while (boxes.Count < PaletteSize)
        {
            ColorBox? widest = null;
            long widestScore = 0;
            foreach (var box in boxes)
            {
                if (box.Keys.Length < 2)
                    continue;
                long score = (long)box.WidestChannel().Span * box.Population;
                if (score > widestScore)
                {
                    widestScore = score;
                    widest = box;
                }
            }
            if (widest == null)
                break;

            int shift = widest.WidestChannel().Shift;
            Array.Sort(widest.Keys, (a, b) => ((a >> shift) & 31).CompareTo((b >> shift) & 31));
            int half = widest.Population / 2, seen = 0, split = 0;
            while (split < widest.Keys.Length - 2 && seen + _counts[widest.Keys[split]] < half)
                seen += _counts[widest.Keys[split++]];
            split++;

            boxes.Remove(widest);
            boxes.Add(CreateBox(widest.Keys[..split]));
            boxes.Add(CreateBox(widest.Keys[split..]));
        }

        Array.Fill(_map, (short)-1);
        Array.Clear(_inPalette);
        _palette = new byte[256 * 3];
        for (int i = 0; i < boxes.Count; i++)
        {
            long r = 0, g = 0, b = 0, population = 0;
            foreach (int key in boxes[i].Keys)
            {
                long count = _counts[key];
                r += ((key >> 10) & 31) * count;
                g += ((key >> 5) & 31) * count;
                b += (key & 31) * count;
                population += count;
                _map[key] = (short)i;
                _inPalette[key] = true;
            }
            _palette[i * 3] = Expand(r / population);
            _palette[i * 3 + 1] = Expand(g / population);
            _palette[i * 3 + 2] = Expand(b / population);
        }
        _paletteCount = boxes.Count;
    }

    private ColorBox CreateBox(int[] keys)
    {
        int population = 0;
        foreach (int key in keys)
            population += _counts[key];
        return new ColorBox { Keys = keys, Population = population };
    }

    /// <summary>Spreads 5 bits over the full 0–255 range, so white stays white.</summary>
    private static byte Expand(long value) => (byte)(value * 255 / 31);

    private byte IndexOf(ushort key)
    {
        short index = _map[key];
        if (index < 0)
            _map[key] = index = Nearest(key);
        return (byte)index;
    }

    private short Nearest(int key)
    {
        int r = Expand((key >> 10) & 31), g = Expand((key >> 5) & 31), b = Expand(key & 31);
        int best = 0, bestDistance = int.MaxValue;
        for (int i = 0; i < _paletteCount; i++)
        {
            int dr = r - _palette[i * 3], dg = g - _palette[i * 3 + 1], db = b - _palette[i * 3 + 2];
            int distance = dr * dr + dg * dg + db * db;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return (short)best;
    }

    private Rectangle ChangedBounds()
    {
        int left = _width, top = _height, right = -1, bottom = -1;
        for (int y = 0, i = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++, i++)
            {
                if (_indices[i] == _previous[i])
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = y;
            }
        }
        return right < 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    /// <summary>The rectangle's pixels; those equal to the frame below become transparent, which compresses far better.</summary>
    private byte[] ExtractPixels(Rectangle bounds, bool full)
    {
        var pixels = new byte[bounds.Width * bounds.Height];
        int target = 0;
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            int source = y * _width + bounds.Left;
            for (int x = 0; x < bounds.Width; x++, source++)
                pixels[target++] = !full && _indices[source] == _previous[source] ? TransparentIndex : _indices[source];
        }
        return pixels;
    }

    // ---------------------------------------------------------------- file format

    private void WriteHeader()
    {
        _output.Write(Encoding.ASCII.GetBytes("GIF89a"));
        WriteUInt16(_width);
        WriteUInt16(_height);
        _output.Write(new byte[] { 0, 0, 0 }); // No global palette: every frame carries its own.

        // Netscape application extension: loop forever.
        _output.Write(new byte[] { 0x21, 0xFF, 11 });
        _output.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        _output.Write(new byte[] { 3, 1, 0, 0, 0 });

        var comment = Encoding.UTF8.GetBytes($"{RecordingMetadata.Tool} {RecordingMetadata.Comment}");
        _output.Write(new byte[] { 0x21, 0xFE, (byte)comment.Length });
        _output.Write(comment);
        _output.WriteByte(0);
    }

    /// <summary>Writes the pending frame, now that it's known how long it stays on screen.</summary>
    private void Flush(long nextTime)
    {
        if (_pending is not { } frame)
            return;
        _pending = null;

        // Delays are rounded against the running total, so the error doesn't add up over the clip.
        long end = Math.Max(_writtenCentiseconds + 2, nextTime / TicksPerCentisecond);
        int delay = (int)Math.Min(end - _writtenCentiseconds, ushort.MaxValue);
        _writtenCentiseconds += delay;

        const byte disposalKeep = 1 << 2, hasTransparency = 1;
        _output.Write(new byte[] { 0x21, 0xF9, 4, disposalKeep | hasTransparency, (byte)delay, (byte)(delay >> 8), TransparentIndex, 0 });

        _output.WriteByte(0x2C);
        WriteUInt16(frame.Bounds.Left);
        WriteUInt16(frame.Bounds.Top);
        WriteUInt16(frame.Bounds.Width);
        WriteUInt16(frame.Bounds.Height);
        _output.WriteByte(0x80 | 7); // Local palette with 2^(7+1) entries.
        _output.Write(frame.Palette);
        WriteLzw(frame.Pixels);
    }

    private void WriteUInt16(int value)
    {
        _output.WriteByte((byte)value);
        _output.WriteByte((byte)(value >> 8));
    }

    private void WriteLzw(byte[] pixels)
    {
        const int minimumCodeSize = 8, clearCode = 1 << minimumCodeSize, endCode = clearCode + 1, maxCode = 4095, tableSize = 1 << 14;

        _output.WriteByte(minimumCodeSize);
        var block = new byte[255];
        int blockLength = 0, pendingBits = 0;
        uint bitBuffer = 0;

        void Emit(int code, int width)
        {
            bitBuffer |= (uint)code << pendingBits;
            pendingBits += width;
            while (pendingBits >= 8)
            {
                block[blockLength++] = (byte)bitBuffer;
                bitBuffer >>= 8;
                pendingBits -= 8;
                if (blockLength == block.Length)
                    FlushBlock();
            }
        }

        void FlushBlock()
        {
            _output.WriteByte((byte)blockLength);
            _output.Write(block, 0, blockLength);
            blockLength = 0;
        }

        // Open-addressed table of (prefix code, next byte) -> code.
        var keys = new int[tableSize];
        var values = new short[tableSize];
        Array.Fill(keys, -1);
        int nextCode = endCode + 1, codeWidth = minimumCodeSize + 1;
        Emit(clearCode, codeWidth);

        int prefix = pixels[0];
        for (int i = 1; i < pixels.Length; i++)
        {
            int value = pixels[i], key = (prefix << 8) | value;
            int slot = (key * 31 + value) & (tableSize - 1);
            while (keys[slot] != -1 && keys[slot] != key)
                slot = (slot + 1) & (tableSize - 1);
            if (keys[slot] == key)
            {
                prefix = values[slot];
                continue;
            }

            Emit(prefix, codeWidth);
            prefix = value;

            int code = nextCode++;
            if (code == 1 << codeWidth && codeWidth < 12)
                codeWidth++;
            if (code == maxCode)
            {
                Emit(clearCode, codeWidth);
                Array.Fill(keys, -1);
                nextCode = endCode + 1;
                codeWidth = minimumCodeSize + 1;
                continue;
            }
            keys[slot] = key;
            values[slot] = (short)code;
        }

        Emit(prefix, codeWidth);
        Emit(endCode, codeWidth);
        if (pendingBits > 0)
            Emit(0, 8 - pendingBits);
        if (blockLength > 0)
            FlushBlock();
        _output.WriteByte(0);
    }
}

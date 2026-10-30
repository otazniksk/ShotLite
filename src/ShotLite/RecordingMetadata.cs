using System.Buffers.Binary;
using System.Reflection;
using System.Text;

namespace ShotLite;

/// <summary>Credits stored in recorded files: the app in the title and "encoded by" tags, the project page in the comment.</summary>
internal static class RecordingMetadata
{
    private const int BoxHeader = 8;
    private const int LargeBoxHeader = 16;
    private const uint Utf8Text = 1;

    public static string Tool => $"{Strings.AppName} {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";

    public static string Comment => AboutForm.RepoUrl;

    /// <summary>
    /// Replaces the sink writer's empty user data box. The sink writer puts the index last, so only the file's
    /// tail changes and the video offsets stay valid; any other layout is left alone rather than risked.
    /// </summary>
    public static void TagMp4(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        if (FindLastBox(file, "moov") is not { } moov)
            return;

        var index = new byte[moov.Length];
        file.Position = moov.Offset;
        file.ReadExactly(index);

        int end = UserDataOffset(index);
        if (end < 0)
            return;

        var userData = CreateUserData();
        var tagged = new byte[end + userData.Length];
        index.AsSpan(0, end).CopyTo(tagged);
        userData.CopyTo(tagged, end);
        BinaryPrimitives.WriteInt32BigEndian(tagged, tagged.Length);

        // Reserve the space first: on a full disk this fails while the file is still intact.
        long length = moov.Offset + tagged.Length;
        if (length > file.Length)
            file.SetLength(length);
        file.Position = moov.Offset;
        file.Write(tagged);
        file.SetLength(length);
    }

    /// <returns>The box when it's the last one in the file, otherwise null.</returns>
    private static (long Offset, int Length)? FindLastBox(FileStream file, string type)
    {
        var header = new byte[LargeBoxHeader];
        for (long offset = 0; offset + BoxHeader <= file.Length;)
        {
            file.Position = offset;
            file.ReadExactly(header, 0, BoxHeader);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (size == 1)
            {
                file.ReadExactly(header, BoxHeader, LargeBoxHeader - BoxHeader);
                size = BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(BoxHeader));
            }
            if (size < BoxHeader)
                return null;

            bool last = offset + size == file.Length;
            if (last && size <= int.MaxValue && Encoding.ASCII.GetString(header, 4, 4) == type)
                return (offset, (int)size);
            offset += size;
        }
        return null;
    }

    /// <returns>Where the new user data goes: over the old box if it's last, at the end if there's none, else -1.</returns>
    private static int UserDataOffset(byte[] index)
    {
        for (int offset = BoxHeader; offset + BoxHeader <= index.Length;)
        {
            int size = BinaryPrimitives.ReadInt32BigEndian(index.AsSpan(offset));
            if (size < BoxHeader || offset + size > index.Length)
                return -1;
            if (Encoding.ASCII.GetString(index, offset + 4, 4) == "udta")
                return offset + size == index.Length ? offset : -1;
            offset += size;
        }
        return index.Length;
    }

    /// <summary>udta / meta / ilst, the iTunes-style tags that players and Windows read.</summary>
    private static byte[] CreateUserData()
    {
        byte[] handlerBody = [.. new byte[8], .. "mdir"u8, .. "appl"u8, .. new byte[9]];
        byte[] items = [.. Tag("nam", Strings.AppName), .. Tag("too", Tool), .. Tag("cmt", Comment)];
        byte[] meta = [.. new byte[4], .. Box("hdlr", handlerBody), .. Box("ilst", items)];
        return Box("udta", Box("meta", meta));
    }

    /// <param name="name">The tag without its leading © byte.</param>
    private static byte[] Tag(string name, string value)
    {
        var flags = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(flags, Utf8Text);
        byte[] data = Box("data", [.. flags, .. Encoding.UTF8.GetBytes(value)]);

        var box = new byte[BoxHeader + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(box, box.Length);
        box[4] = 0xA9;
        Encoding.ASCII.GetBytes(name, box.AsSpan(5));
        data.CopyTo(box, BoxHeader);
        return box;
    }

    private static byte[] Box(string type, byte[] content)
    {
        var box = new byte[BoxHeader + content.Length];
        BinaryPrimitives.WriteInt32BigEndian(box, box.Length);
        Encoding.ASCII.GetBytes(type, box.AsSpan(4));
        content.CopyTo(box, BoxHeader);
        return box;
    }
}

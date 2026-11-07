using System;
using System.IO;
using System.Threading.Tasks;
using static BBDown.Core.Logger;

namespace BBDown;

internal static class BBDownCoverArt
{
    // Inspect the bytes rather than the URL extension: B站 may serve WebP as .jpg.
    internal static bool HasImageSignature(string file)
    {
        if (!File.Exists(file)) return false;
        using var input = File.OpenRead(file);
        if (input.Length < 12) return false;
        Span<byte> head = stackalloc byte[12];
        input.ReadExactly(head);
        input.Seek(-12, SeekOrigin.End);
        Span<byte> tail = stackalloc byte[12];
        input.ReadExactly(tail);
        if (head[0] == 0xff && head[1] == 0xd8 && head[2] == 0xff)
            return tail[^2] == 0xff && tail[^1] == 0xd9;
        if (head[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return tail[4..8].SequenceEqual("IEND"u8);
        if (head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
            return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(head[4..8]) == input.Length - 8;
        return (head[..6].SequenceEqual("GIF87a"u8) || head[..6].SequenceEqual("GIF89a"u8)) && tail[^1] == 0x3b;
    }

    internal static async Task<bool> EnsureAsync(string file, string url,
        Func<string, string, Task> download, Func<string, bool> readable, bool required = false)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(file) ?? "",
            Path.GetFileNameWithoutExtension(file) + ".cover-" + Guid.NewGuid().ToString("N") + Path.GetExtension(file));
        try
        {
            if (File.Exists(file) && readable(file)) return true;
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidDataException("没有可用的封面地址");
            await download(url, temporary);
            if (!readable(temporary)) throw new InvalidDataException("封面不是完整可读的图片");
            File.Move(temporary, file, true);
            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException && !required)
        {
            LogWarn($"封面暂不可用（{error.GetType().Name}），继续归档音视频但不嵌入封面");
            return false;
        }
        finally
        {
            foreach (var owned in new[] { temporary, Path.ChangeExtension(temporary, ".tmp") })
            {
                try { if (File.Exists(owned)) File.Delete(owned); }
                catch (IOException) { LogWarn("封面临时文件清理失败"); }
                catch (UnauthorizedAccessException) { LogWarn("封面临时文件清理失败"); }
            }
        }
    }
}

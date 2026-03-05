using System.Text;

namespace DaisiGit.Core.Git.Protocol;

/// <summary>
/// Git pkt-line wire protocol encoding/decoding.
/// See: https://git-scm.com/docs/protocol-common#_pkt_line_format
/// </summary>
public static class PktLine
{
    /// <summary>Flush packet (0000).</summary>
    public static readonly byte[] Flush = "0000"u8.ToArray();

    /// <summary>Delimiter packet (0001) — used in protocol v2.</summary>
    public static readonly byte[] Delim = "0001"u8.ToArray();

    /// <summary>
    /// Encodes a string as a pkt-line (4-hex-digit length prefix + data + LF).
    /// </summary>
    public static byte[] Encode(string data)
    {
        var payload = Encoding.UTF8.GetBytes(data + "\n");
        var length = payload.Length + 4; // 4 bytes for the length prefix itself
        var prefix = Encoding.ASCII.GetBytes($"{length:x4}");
        var result = new byte[prefix.Length + payload.Length];
        Buffer.BlockCopy(prefix, 0, result, 0, 4);
        Buffer.BlockCopy(payload, 0, result, 4, payload.Length);
        return result;
    }

    /// <summary>
    /// Encodes raw bytes as a pkt-line (4-hex-digit length prefix + data, no trailing LF).
    /// </summary>
    public static byte[] EncodeRaw(byte[] data)
    {
        var length = data.Length + 4;
        var prefix = Encoding.ASCII.GetBytes($"{length:x4}");
        var result = new byte[prefix.Length + data.Length];
        Buffer.BlockCopy(prefix, 0, result, 0, 4);
        Buffer.BlockCopy(data, 0, result, 4, data.Length);
        return result;
    }

    /// <summary>
    /// Reads all pkt-lines from a stream until a flush packet or end of stream.
    /// Returns the lines as strings (without length prefix, with trailing LF stripped).
    /// </summary>
    public static async Task<List<string>> ReadAllLinesAsync(Stream stream)
    {
        var lines = new List<string>();
        var lenBuf = new byte[4];

        while (true)
        {
            var bytesRead = await ReadExactAsync(stream, lenBuf, 4);
            if (bytesRead < 4)
                break;

            var lenStr = Encoding.ASCII.GetString(lenBuf, 0, 4);

            // Flush packet
            if (lenStr == "0000")
                break;

            // Delimiter packet
            if (lenStr == "0001")
            {
                lines.Add(""); // Empty string marks delimiter
                continue;
            }

            var len = Convert.ToInt32(lenStr, 16);
            if (len <= 4) continue; // Minimal packet

            var dataLen = len - 4;
            var data = new byte[dataLen];
            await ReadExactAsync(stream, data, dataLen);

            var line = Encoding.UTF8.GetString(data).TrimEnd('\n');
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>
    /// Reads all remaining bytes from a stream after pkt-line negotiation (for pack data).
    /// </summary>
    public static async Task<byte[]> ReadPackDataAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        var lenBuf = new byte[4];

        while (true)
        {
            var bytesRead = await ReadExactAsync(stream, lenBuf, 4);
            if (bytesRead < 4)
                break;

            var lenStr = Encoding.ASCII.GetString(lenBuf, 0, 4);
            if (lenStr == "0000")
                break;

            var len = Convert.ToInt32(lenStr, 16);
            if (len <= 4) continue;

            var dataLen = len - 4;
            var data = new byte[dataLen];
            await ReadExactAsync(stream, data, dataLen);

            // Skip sideband byte if present (byte 1 = pack data, 2 = progress, 3 = error)
            if (data.Length > 0 && data[0] >= 1 && data[0] <= 3)
            {
                ms.Write(data, 1, data.Length - 1);
            }
            else
            {
                ms.Write(data);
            }
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Writes multiple pkt-lines followed by a flush.
    /// </summary>
    public static void WriteLines(Stream stream, IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var encoded = Encode(line);
            stream.Write(encoded);
        }
        stream.Write(Flush);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int count)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead));
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead;
    }
}

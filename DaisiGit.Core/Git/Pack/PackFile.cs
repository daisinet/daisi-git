using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DaisiGit.Core.Git.Pack;

/// <summary>
/// Git pack file generator and parser.
/// Pack format: https://git-scm.com/docs/pack-format
/// </summary>
public static class PackFile
{
    private const uint PackSignature = 0x5041434B; // "PACK"
    private const uint PackVersion = 2;

    // Pack object type numbers (different from GitObjectType enum values)
    private const int OBJ_COMMIT = 1;
    private const int OBJ_TREE = 2;
    private const int OBJ_BLOB = 3;
    private const int OBJ_TAG = 4;
    private const int OBJ_OFS_DELTA = 6;
    private const int OBJ_REF_DELTA = 7;

    /// <summary>
    /// Generates a pack file from a list of git objects.
    /// </summary>
    public static byte[] Generate(IReadOnlyList<GitObject> objects)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        // Header: "PACK", version 2, object count
        writer.Write(ToBigEndian(PackSignature));
        writer.Write(ToBigEndian(PackVersion));
        writer.Write(ToBigEndian((uint)objects.Count));

        foreach (var obj in objects)
        {
            var content = obj.SerializeContent();
            var packType = ToPackType(obj.Type);
            WritePackEntry(ms, packType, content);
        }

        // Trailing SHA-1 checksum of everything written so far
        ms.Flush();
        var data = ms.ToArray();
        var checksum = SHA1.HashData(data);
        ms.Write(checksum);

        return ms.ToArray();
    }

    /// <summary>
    /// Parses a pack file into individual entries (raw content, not full objects).
    /// </summary>
    public static List<PackEntry> Parse(byte[] packData)
    {
        var entries = new List<PackEntry>();
        var offset = 0;

        // Verify header
        var sig = ReadBigEndianUInt32(packData, offset); offset += 4;
        if (sig != PackSignature)
            throw new FormatException("Invalid pack signature");

        var version = ReadBigEndianUInt32(packData, offset); offset += 4;
        if (version != 2)
            throw new FormatException($"Unsupported pack version: {version}");

        var count = (int)ReadBigEndianUInt32(packData, offset); offset += 4;

        for (var i = 0; i < count; i++)
        {
            var entryStart = offset;
            var (packType, size, newOffset) = ReadTypeAndSize(packData, offset);
            offset = newOffset;

            var entry = new PackEntry();

            if (packType == OBJ_REF_DELTA)
            {
                // 20-byte base object SHA
                var baseShaBytes = new byte[20];
                Buffer.BlockCopy(packData, offset, baseShaBytes, 0, 20);
                entry.BaseSha = ObjectHasher.BytesToSha(baseShaBytes);
                offset += 20;

                var deltaData = DeflateFromPack(packData, offset, out var bytesConsumed);
                entry.DeltaData = deltaData;
                entry.ObjectType = GitObjectType.Blob; // Will be resolved later
                offset += bytesConsumed;
            }
            else if (packType == OBJ_OFS_DELTA)
            {
                // Variable-length negative offset
                long baseOffset = packData[offset] & 0x7F;
                while ((packData[offset] & 0x80) != 0)
                {
                    offset++;
                    baseOffset = ((baseOffset + 1) << 7) | (long)(packData[offset] & 0x7F);
                }
                offset++;
                entry.BaseOffset = entryStart - (int)baseOffset;

                var deltaData = DeflateFromPack(packData, offset, out var bytesConsumed);
                entry.DeltaData = deltaData;
                entry.ObjectType = GitObjectType.Blob; // Will be resolved later
                offset += bytesConsumed;
            }
            else
            {
                entry.ObjectType = FromPackType(packType);
                var data = DeflateFromPack(packData, offset, out var bytesConsumed);
                entry.Data = data;
                offset += bytesConsumed;

                // Compute SHA from the full git object
                var typeStr = entry.ObjectType switch
                {
                    GitObjectType.Commit => "commit",
                    GitObjectType.Tree => "tree",
                    GitObjectType.Blob => "blob",
                    GitObjectType.Tag => "tag",
                    _ => throw new NotSupportedException()
                };
                var header = Encoding.ASCII.GetBytes($"{typeStr} {data.Length}\0");
                var full = new byte[header.Length + data.Length];
                Buffer.BlockCopy(header, 0, full, 0, header.Length);
                Buffer.BlockCopy(data, 0, full, header.Length, data.Length);
                entry.Sha = ObjectHasher.HashRaw(full);
            }

            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// Applies a git delta to a base object to produce the result.
    /// </summary>
    public static byte[] ApplyDelta(byte[] baseData, byte[] delta)
    {
        var offset = 0;

        // Read base object size (variable-length)
        ReadDeltaSize(delta, ref offset);

        // Read result size (variable-length)
        var resultSize = ReadDeltaSize(delta, ref offset);
        var result = new byte[resultSize];
        var resultOffset = 0;

        while (offset < delta.Length)
        {
            var cmd = delta[offset++];
            if ((cmd & 0x80) != 0)
            {
                // Copy from base
                long copyOffset = 0;
                long copySize = 0;
                if ((cmd & 0x01) != 0) copyOffset = delta[offset++];
                if ((cmd & 0x02) != 0) copyOffset |= (long)delta[offset++] << 8;
                if ((cmd & 0x04) != 0) copyOffset |= (long)delta[offset++] << 16;
                if ((cmd & 0x08) != 0) copyOffset |= (long)delta[offset++] << 24;
                if ((cmd & 0x10) != 0) copySize = delta[offset++];
                if ((cmd & 0x20) != 0) copySize |= (long)delta[offset++] << 8;
                if ((cmd & 0x40) != 0) copySize |= (long)delta[offset++] << 16;
                if (copySize == 0) copySize = 0x10000;

                Buffer.BlockCopy(baseData, (int)copyOffset, result, resultOffset, (int)copySize);
                resultOffset += (int)copySize;
            }
            else if (cmd > 0)
            {
                // Insert new data
                Buffer.BlockCopy(delta, offset, result, resultOffset, cmd);
                offset += cmd;
                resultOffset += cmd;
            }
        }

        return result;
    }

    private static void WritePackEntry(Stream stream, int packType, byte[] content)
    {
        // Write type + size header (variable-length encoding)
        var size = (long)content.Length;
        var firstByte = (byte)((packType << 4) | (int)(size & 0x0F));
        size >>= 4;
        if (size > 0) firstByte |= 0x80;
        stream.WriteByte(firstByte);

        while (size > 0)
        {
            var b = (byte)(size & 0x7F);
            size >>= 7;
            if (size > 0) b |= 0x80;
            stream.WriteByte(b);
        }

        // Write zlib-compressed content
        if (content.Length == 0)
        {
            // ZLibStream produces no output for empty input, but git expects a valid
            // zlib stream. Write the canonical empty zlib stream: header + empty deflate + adler32.
            stream.Write(new byte[] { 0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01 });
        }
        else
        {
            using var zlibOutput = new MemoryStream();
            using (var zlib = new ZLibStream(zlibOutput, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(content);
            }
            stream.Write(zlibOutput.ToArray());
        }
    }

    private static (int type, long size, int newOffset) ReadTypeAndSize(byte[] data, int offset)
    {
        var b = data[offset++];
        var type = (b >> 4) & 0x07;
        long size = b & 0x0F;
        var shift = 4;

        while ((b & 0x80) != 0)
        {
            b = data[offset++];
            size |= (long)(b & 0x7F) << shift;
            shift += 7;
        }

        return (type, size, offset);
    }

    private static byte[] DeflateFromPack(byte[] data, int offset, out int bytesConsumed)
    {
        // Pack entries use zlib (2-byte header + deflate data + 4-byte Adler32).
        // .NET's DeflateStream/ZLibStream buffer internally so stream position overshoots.
        //
        // Strategy: skip the zlib header, feed data ONE BYTE AT A TIME to DeflateStream.
        // When DeflateStream finishes, the single-byte stream position is at most 1 byte
        // past the true deflate end. The Adler32 MUST be at either (position) or (position-1)
        // relative to the post-header data. We check both positions.
        var headerSize = 2;
        var remaining = data.Length - offset - headerSize;

        // Decompress using single-byte reads to minimize over-read
        var sbs = new SingleByteReadStream(data, offset + headerSize, remaining);
        byte[] decompressed;
        using (var deflate = new DeflateStream(sbs, CompressionMode.Decompress, leaveOpen: true))
        using (var output = new MemoryStream())
        {
            var buf = new byte[4096];
            int n;
            while ((n = deflate.Read(buf, 0, buf.Length)) > 0)
                output.Write(buf, 0, n);
            decompressed = output.ToArray();
        }

        var deflatePos = (int)sbs.Position; // position in post-header data
        var expectedAdler = ComputeAdler32(decompressed);

        // The Adler32 starts right after the deflate data. DeflateStream with single-byte
        // reads consumes the exact deflate data plus 0-1 extra bytes. So the Adler32 starts
        // at (deflatePos - overread) where overread is 0 or 1.
        // Search from (deflatePos - 1) forward to find the first Adler32 match.
        for (var adlerStart = Math.Max(0, deflatePos - 1); adlerStart <= deflatePos; adlerStart++)
        {
            var absPos = offset + headerSize + adlerStart;
            if (absPos + 4 > data.Length) continue;
            var candidate = (uint)((data[absPos] << 24) | (data[absPos + 1] << 16) |
                                    (data[absPos + 2] << 8) | data[absPos + 3]);
            if (candidate == expectedAdler)
            {
                bytesConsumed = headerSize + adlerStart + 4;
                return decompressed;
            }
        }

        // Fallback: Adler32 is right after the deflate stream position + 4
        bytesConsumed = headerSize + deflatePos + 4;
        return decompressed;
    }

    /// <summary>
    /// A read-only stream that yields at most one byte per Read call.
    /// Prevents DeflateStream from buffering past the end of the compressed data.
    /// </summary>
    private sealed class SingleByteReadStream(byte[] data, int start, int length) : Stream
    {
        private int _pos;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0 || _pos >= length) return 0;
            buffer[offset] = data[start + _pos];
            _pos++;
            return 1;
        }
        public override long Position { get => _pos; set => _pos = (int)value; }
        public override long Length => length;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Seek(long o, SeekOrigin origin) => _pos = origin switch
        {
            SeekOrigin.Begin => (int)o, SeekOrigin.Current => _pos + (int)o,
            SeekOrigin.End => length + (int)o, _ => _pos
        };
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) { }
        public override void Flush() { }
    }

    private static long ReadDeltaSize(byte[] data, ref int offset)
    {
        long size = 0;
        var shift = 0;
        byte b;
        do
        {
            b = data[offset++];
            size |= (long)(b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return size;
    }

    private static int ToPackType(GitObjectType type) => type switch
    {
        GitObjectType.Commit => OBJ_COMMIT,
        GitObjectType.Tree => OBJ_TREE,
        GitObjectType.Blob => OBJ_BLOB,
        GitObjectType.Tag => OBJ_TAG,
        _ => throw new ArgumentException($"Cannot pack type: {type}")
    };

    private static GitObjectType FromPackType(int packType) => packType switch
    {
        OBJ_COMMIT => GitObjectType.Commit,
        OBJ_TREE => GitObjectType.Tree,
        OBJ_BLOB => GitObjectType.Blob,
        OBJ_TAG => GitObjectType.Tag,
        _ => throw new ArgumentException($"Unknown pack type: {packType}")
    };

    private static byte[] ToBigEndian(uint value) =>
    [
        (byte)(value >> 24),
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value
    ];

    private static uint ReadBigEndianUInt32(byte[] data, int offset) =>
        ((uint)data[offset] << 24) |
        ((uint)data[offset + 1] << 16) |
        ((uint)data[offset + 2] << 8) |
        data[offset + 3];

    private static uint ComputeAdler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var d in data)
        {
            a = (a + d) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }
}

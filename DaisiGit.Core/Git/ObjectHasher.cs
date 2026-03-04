using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DaisiGit.Core.Git;

/// <summary>
/// Utilities for SHA-1 hashing and zlib compression of git objects.
/// </summary>
public static class ObjectHasher
{
    /// <summary>
    /// Computes the SHA-1 hash of a full git object (header + content).
    /// </summary>
    public static string HashObject(GitObject obj)
    {
        var raw = obj.Serialize();
        var hash = SHA1.HashData(raw);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Computes SHA-1 for raw bytes (including git header).
    /// </summary>
    public static string HashRaw(byte[] rawObject)
    {
        var hash = SHA1.HashData(rawObject);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Zlib-compresses data (for loose object storage).
    /// </summary>
    public static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        // Write zlib header (deflate, default compression)
        output.WriteByte(0x78);
        output.WriteByte(0x9C);
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }
        // Write Adler-32 checksum
        var adler = ComputeAdler32(data);
        output.WriteByte((byte)(adler >> 24));
        output.WriteByte((byte)(adler >> 16));
        output.WriteByte((byte)(adler >> 8));
        output.WriteByte((byte)adler);
        return output.ToArray();
    }

    /// <summary>
    /// Zlib-decompresses data (from loose object storage).
    /// </summary>
    public static byte[] ZlibDecompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        // Skip 2-byte zlib header
        input.ReadByte();
        input.ReadByte();
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// Parses a raw (decompressed) git object into header components and content.
    /// </summary>
    public static (string type, int size, byte[] content) ParseRawObject(byte[] raw)
    {
        var nullIdx = Array.IndexOf(raw, (byte)0);
        if (nullIdx < 0)
            throw new FormatException("Invalid git object: no null byte found in header");

        var header = Encoding.ASCII.GetString(raw, 0, nullIdx);
        var spaceIdx = header.IndexOf(' ');
        var type = header[..spaceIdx];
        var size = int.Parse(header[(spaceIdx + 1)..]);

        var content = new byte[raw.Length - nullIdx - 1];
        Buffer.BlockCopy(raw, nullIdx + 1, content, 0, content.Length);
        return (type, size, content);
    }

    /// <summary>
    /// Parses a decompressed raw git object into a typed GitObject.
    /// </summary>
    public static GitObject ParseObject(byte[] raw)
    {
        var (type, _, content) = ParseRawObject(raw);
        GitObject obj = type switch
        {
            "blob" => GitBlob.Parse(content),
            "tree" => GitTree.Parse(content),
            "commit" => GitCommit.Parse(content),
            "tag" => GitTag.Parse(content),
            _ => throw new NotSupportedException($"Unknown object type: {type}")
        };
        obj.Sha = HashRaw(raw);
        return obj;
    }

    /// <summary>
    /// Converts a hex SHA string to 20 raw bytes.
    /// </summary>
    public static byte[] ShaToBytes(string sha)
    {
        return Convert.FromHexString(sha);
    }

    /// <summary>
    /// Converts 20 raw bytes to a hex SHA string.
    /// </summary>
    public static string BytesToSha(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

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

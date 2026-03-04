using DaisiGit.Core.Git;
using DaisiGit.Core.Git.Pack;

namespace DaisiGit.Tests;

public class PackFileTests
{
    [Fact]
    public void Generate_ProducesValidPackHeader()
    {
        var blob = new GitBlob { Data = "test"u8.ToArray() };
        blob.Sha = ObjectHasher.HashObject(blob);

        var packData = PackFile.Generate([blob]);

        // Verify PACK signature
        Assert.Equal((byte)'P', packData[0]);
        Assert.Equal((byte)'A', packData[1]);
        Assert.Equal((byte)'C', packData[2]);
        Assert.Equal((byte)'K', packData[3]);

        // Verify version 2
        Assert.Equal(0, packData[4]);
        Assert.Equal(0, packData[5]);
        Assert.Equal(0, packData[6]);
        Assert.Equal(2, packData[7]);

        // Verify object count = 1
        Assert.Equal(0, packData[8]);
        Assert.Equal(0, packData[9]);
        Assert.Equal(0, packData[10]);
        Assert.Equal(1, packData[11]);
    }

    [Fact]
    public void Generate_Parse_RoundTrips()
    {
        var blob1 = new GitBlob { Data = "Hello World\n"u8.ToArray() };
        blob1.Sha = ObjectHasher.HashObject(blob1);

        var blob2 = new GitBlob { Data = "Another file\n"u8.ToArray() };
        blob2.Sha = ObjectHasher.HashObject(blob2);

        var packData = PackFile.Generate([blob1, blob2]);
        var entries = PackFile.Parse(packData);

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(GitObjectType.Blob, e.ObjectType));
    }

    [Fact]
    public void ApplyDelta_CopyInstruction_Works()
    {
        var baseData = "Hello, World!"u8.ToArray();

        // Simple delta: copy all from base
        var delta = new byte[]
        {
            13, // base size
            13, // result size
            0x80 | 0x10 | 0x01, // copy: offset present (bit 0), size present (bit 4)
            0, // offset = 0
            13 // size = 13
        };

        var result = PackFile.ApplyDelta(baseData, delta);
        Assert.Equal(baseData, result);
    }

    [Fact]
    public void ApplyDelta_InsertInstruction_Works()
    {
        var baseData = "Hello"u8.ToArray();

        // Delta: insert new data ", World!"
        var insertData = ", World!"u8.ToArray();
        var delta = new List<byte>
        {
            5, // base size = 5
            13 // result size = 13
        };
        // Copy "Hello" from base
        delta.Add(0x80 | 0x10 | 0x01); // copy
        delta.Add(0); // offset = 0
        delta.Add(5); // size = 5
        // Insert ", World!"
        delta.Add((byte)insertData.Length); // insert cmd
        delta.AddRange(insertData);

        var result = PackFile.ApplyDelta(baseData, delta.ToArray());
        Assert.Equal("Hello, World!", System.Text.Encoding.UTF8.GetString(result));
    }
}

using DaisiGit.Core.Git.Protocol;

namespace DaisiGit.Tests;

public class PktLineTests
{
    [Fact]
    public void Encode_ProducesCorrectLengthPrefix()
    {
        var result = PktLine.Encode("hello");
        // "hello" + "\n" = 6 bytes data, + 4 prefix = 10 = "000a"
        var prefix = System.Text.Encoding.ASCII.GetString(result, 0, 4);
        Assert.Equal("000a", prefix);
    }

    [Fact]
    public void Encode_IncludesTrailingNewline()
    {
        var result = PktLine.Encode("test");
        var data = System.Text.Encoding.UTF8.GetString(result, 4, result.Length - 4);
        Assert.Equal("test\n", data);
    }

    [Fact]
    public async Task ReadAllLinesAsync_ParsesMultipleLines()
    {
        using var ms = new MemoryStream();
        ms.Write(PktLine.Encode("line one"));
        ms.Write(PktLine.Encode("line two"));
        ms.Write(PktLine.Flush);
        ms.Position = 0;

        var lines = await PktLine.ReadAllLinesAsync(ms);
        Assert.Equal(2, lines.Count);
        Assert.Equal("line one", lines[0]);
        Assert.Equal("line two", lines[1]);
    }

    [Fact]
    public async Task ReadAllLinesAsync_StopsAtFlush()
    {
        using var ms = new MemoryStream();
        ms.Write(PktLine.Encode("before flush"));
        ms.Write(PktLine.Flush);
        ms.Write(PktLine.Encode("after flush"));
        ms.Position = 0;

        var lines = await PktLine.ReadAllLinesAsync(ms);
        Assert.Single(lines);
        Assert.Equal("before flush", lines[0]);
    }
}

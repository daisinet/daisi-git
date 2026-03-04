using DaisiGit.Core.Git;

namespace DaisiGit.Tests;

public class GitObjectTests
{
    [Fact]
    public void GitBlob_Serialize_ProducesCorrectFormat()
    {
        var blob = new GitBlob { Data = "Hello, World!\n"u8.ToArray() };
        var raw = blob.Serialize();
        var header = System.Text.Encoding.ASCII.GetString(raw, 0, raw.Length - blob.Data.Length);
        Assert.Equal("blob 14\0", header);
    }

    [Fact]
    public void GitBlob_HashObject_ProducesValidSha()
    {
        var blob = new GitBlob { Data = "Hello, World!\n"u8.ToArray() };
        var sha = ObjectHasher.HashObject(blob);
        Assert.Equal(40, sha.Length);
        Assert.Matches("^[0-9a-f]{40}$", sha);
    }

    [Fact]
    public void GitBlob_RoundTrip_PreservesContent()
    {
        var original = new GitBlob { Data = "test content\n"u8.ToArray() };
        var raw = original.Serialize();
        var parsed = ObjectHasher.ParseObject(raw);
        Assert.IsType<GitBlob>(parsed);
        Assert.Equal(original.Data, ((GitBlob)parsed).Data);
    }

    [Fact]
    public void GitTree_RoundTrip_PreservesEntries()
    {
        var tree = new GitTree
        {
            Entries =
            [
                new GitTreeEntry { Mode = "100644", Name = "README.md", Sha = "abcdef1234567890abcdef1234567890abcdef12" },
                new GitTreeEntry { Mode = "040000", Name = "src", Sha = "1234567890abcdef1234567890abcdef12345678" }
            ]
        };

        var raw = tree.Serialize();
        var parsed = (GitTree)ObjectHasher.ParseObject(raw);
        Assert.Equal(2, parsed.Entries.Count);
        Assert.Contains(parsed.Entries, e => e.Name == "README.md" && e.Mode == "100644");
        Assert.Contains(parsed.Entries, e => e.Name == "src" && e.Mode == "040000" && e.IsTree);
    }

    [Fact]
    public void GitCommit_RoundTrip_PreservesFields()
    {
        var commit = new GitCommit
        {
            TreeSha = "abcdef1234567890abcdef1234567890abcdef12",
            ParentShas = ["1234567890abcdef1234567890abcdef12345678"],
            Author = new GitSignature
            {
                Name = "Test User",
                Email = "test@example.com",
                Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            },
            Committer = new GitSignature
            {
                Name = "Test User",
                Email = "test@example.com",
                Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            },
            Message = "Initial commit\n"
        };

        var raw = commit.Serialize();
        var parsed = (GitCommit)ObjectHasher.ParseObject(raw);
        Assert.Equal(commit.TreeSha, parsed.TreeSha);
        Assert.Equal(commit.ParentShas, parsed.ParentShas);
        Assert.Equal(commit.Message, parsed.Message);
        Assert.Equal("Test User", parsed.Author.Name);
        Assert.Equal("test@example.com", parsed.Author.Email);
    }

    [Fact]
    public void GitSignature_RoundTrip_PreservesTimezone()
    {
        var sig = new GitSignature
        {
            Name = "John Doe",
            Email = "john@example.com",
            Timestamp = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.FromHours(-5))
        };

        var str = sig.ToString();
        var parsed = GitSignature.Parse(str);
        Assert.Equal("John Doe", parsed.Name);
        Assert.Equal("john@example.com", parsed.Email);
        Assert.Equal(sig.Timestamp.ToUnixTimeSeconds(), parsed.Timestamp.ToUnixTimeSeconds());
    }

    [Fact]
    public void ZlibCompress_Decompress_RoundTrips()
    {
        var data = "Hello, World! This is a test of zlib compression."u8.ToArray();
        var compressed = ObjectHasher.ZlibCompress(data);
        var decompressed = ObjectHasher.ZlibDecompress(compressed);
        Assert.Equal(data, decompressed);
    }

    [Fact]
    public void ShaToBytes_BytesToSha_RoundTrips()
    {
        var sha = "abcdef1234567890abcdef1234567890abcdef12";
        var bytes = ObjectHasher.ShaToBytes(sha);
        Assert.Equal(20, bytes.Length);
        var result = ObjectHasher.BytesToSha(bytes);
        Assert.Equal(sha, result);
    }
}

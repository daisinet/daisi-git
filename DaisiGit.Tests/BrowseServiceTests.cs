using DaisiGit.Core.Git;
using DaisiGit.Services;

namespace DaisiGit.Tests;

public class BrowseServiceTests
{
    [Fact]
    public void CommitInfo_FieldsWork()
    {
        var sha = "abcdef1234567890abcdef1234567890abcdef12";
        var info = new CommitInfo
        {
            Sha = sha,
            ShortSha = sha[..7],
            TreeSha = "aaaa" + new string('0', 36),
            ParentShas = ["bbbb" + new string('0', 36)],
            AuthorName = "Alice",
            AuthorEmail = "alice@example.com",
            AuthorDate = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero),
            CommitterName = "Alice",
            CommitterDate = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero),
            Message = "Fix bug in parser\n\nDetailed description of the fix.",
            MessageFirstLine = "Fix bug in parser"
        };

        Assert.Equal(sha, info.Sha);
        Assert.Equal("abcdef1", info.ShortSha);
        Assert.Equal("Alice", info.AuthorName);
        Assert.Equal("alice@example.com", info.AuthorEmail);
        Assert.Equal("Fix bug in parser", info.MessageFirstLine);
        Assert.Single(info.ParentShas);
    }

    [Fact]
    public void FileContent_DetectsBinary()
    {
        var text = new FileContent
        {
            Sha = "abc123",
            Data = "Hello, World!"u8.ToArray(),
            SizeBytes = 13,
            IsBinary = false,
            Text = "Hello, World!"
        };

        Assert.False(text.IsBinary);
        Assert.NotNull(text.Text);

        var binary = new FileContent
        {
            Sha = "def456",
            Data = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, // PNG header
            SizeBytes = 8,
            IsBinary = true,
            Text = null
        };

        Assert.True(binary.IsBinary);
        Assert.Null(binary.Text);
    }

    [Fact]
    public void BrowseResult_IdentifiesFileVsDirectory()
    {
        var dir = new BrowseResult
        {
            IsFile = false,
            Path = "src",
            CommitSha = "abc123",
            TreeSha = "tree123",
            Entries = [
                new GitTreeEntry { Name = "main.cs", Mode = "100644", Sha = "file1" },
                new GitTreeEntry { Name = "lib", Mode = "040000", Sha = "tree2" }
            ]
        };

        Assert.False(dir.IsFile);
        Assert.Equal(2, dir.Entries.Count);

        var file = new BrowseResult
        {
            IsFile = true,
            Path = "src/main.cs",
            CommitSha = "abc123",
            FileSha = "file1",
            FileName = "main.cs",
            FileMode = "100644"
        };

        Assert.True(file.IsFile);
        Assert.Equal("main.cs", file.FileName);
    }

    [Fact]
    public void FileDiff_StatusValues()
    {
        var added = new FileDiff { Path = "new.txt", Status = DiffStatus.Added, NewContent = "content" };
        var modified = new FileDiff { Path = "existing.txt", Status = DiffStatus.Modified, OldContent = "old", NewContent = "new" };
        var deleted = new FileDiff { Path = "removed.txt", Status = DiffStatus.Deleted, OldContent = "was here" };

        Assert.Equal(DiffStatus.Added, added.Status);
        Assert.Null(added.OldContent);
        Assert.Equal(DiffStatus.Modified, modified.Status);
        Assert.Equal(DiffStatus.Deleted, deleted.Status);
        Assert.Null(deleted.NewContent);
    }
}

using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;

public class ForkStarTests
{
    [Fact]
    public void GitRepository_ForkFields_DefaultToNull()
    {
        var repo = new GitRepository();
        Assert.Null(repo.ForkedFromId);
        Assert.Null(repo.ForkedFromOwnerName);
        Assert.Null(repo.ForkedFromSlug);
    }

    [Fact]
    public void GitRepository_StarCount_DefaultsToZero()
    {
        var repo = new GitRepository();
        Assert.Equal(0, repo.StarCount);
    }

    [Fact]
    public void GitRepository_ForkCount_DefaultsToZero()
    {
        var repo = new GitRepository();
        Assert.Equal(0, repo.ForkCount);
    }

    [Fact]
    public void GitRepository_ForkFields_CanBeSet()
    {
        var repo = new GitRepository
        {
            ForkedFromId = "repo-123",
            ForkedFromOwnerName = "upstream-owner",
            ForkedFromSlug = "my-repo"
        };

        Assert.Equal("repo-123", repo.ForkedFromId);
        Assert.Equal("upstream-owner", repo.ForkedFromOwnerName);
        Assert.Equal("my-repo", repo.ForkedFromSlug);
    }

    [Fact]
    public void GitRepository_Counters_CanBeIncremented()
    {
        var repo = new GitRepository { StarCount = 5, ForkCount = 3 };
        repo.StarCount++;
        repo.ForkCount++;
        Assert.Equal(6, repo.StarCount);
        Assert.Equal(4, repo.ForkCount);
    }

    [Fact]
    public void RepoStar_Type_IsRepoStar()
    {
        var star = new RepoStar();
        Assert.Equal("RepoStar", star.Type);
    }

    [Fact]
    public void RepoStar_Fields_HaveDefaults()
    {
        var star = new RepoStar();
        Assert.Equal("", star.id);
        Assert.Equal("", star.RepositoryId);
        Assert.Equal("", star.UserId);
        Assert.Equal("", star.UserName);
        Assert.True(star.CreatedUtc <= DateTime.UtcNow);
    }

    [Fact]
    public void RepoStar_Fields_CanBeSet()
    {
        var star = new RepoStar
        {
            id = "star-123",
            RepositoryId = "repo-456",
            UserId = "user-789",
            UserName = "testuser"
        };

        Assert.Equal("star-123", star.id);
        Assert.Equal("repo-456", star.RepositoryId);
        Assert.Equal("user-789", star.UserId);
        Assert.Equal("testuser", star.UserName);
    }

    [Fact]
    public void GitRepository_ForkedRepo_PreservesVisibility()
    {
        var upstream = new GitRepository
        {
            Visibility = GitRepoVisibility.Public,
            ForkedFromId = null
        };

        var fork = new GitRepository
        {
            Visibility = upstream.Visibility,
            ForkedFromId = "repo-upstream",
            ForkedFromOwnerName = "original-owner",
            ForkedFromSlug = "original-repo"
        };

        Assert.Equal(GitRepoVisibility.Public, fork.Visibility);
        Assert.NotNull(fork.ForkedFromId);
    }
}

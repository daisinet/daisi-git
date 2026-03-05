using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;

namespace DaisiGit.Tests;

public class ModelTests
{
    [Fact]
    public void GitRole_Hierarchy_IsCorrectlyOrdered()
    {
        Assert.True(GitRole.Owner > GitRole.Admin);
        Assert.True(GitRole.Admin > GitRole.Maintain);
        Assert.True(GitRole.Maintain > GitRole.Write);
        Assert.True(GitRole.Write > GitRole.Read);
    }

    [Fact]
    public void GitPermissionLevel_Hierarchy_IsCorrectlyOrdered()
    {
        Assert.True(GitPermissionLevel.Admin > GitPermissionLevel.Write);
        Assert.True(GitPermissionLevel.Write > GitPermissionLevel.Read);
        Assert.True(GitPermissionLevel.Read > GitPermissionLevel.None);
    }

    [Fact]
    public void Issue_DefaultStatus_IsOpen()
    {
        var issue = new Issue();
        Assert.Equal(IssueStatus.Open, issue.Status);
    }

    [Fact]
    public void PullRequest_DefaultStatus_IsOpen()
    {
        var pr = new PullRequest();
        Assert.Equal(PullRequestStatus.Open, pr.Status);
    }

    [Fact]
    public void GitRepository_DefaultBranch_IsMain()
    {
        var repo = new GitRepository();
        Assert.Equal("main", repo.DefaultBranch);
    }

    [Fact]
    public void GitRepository_DefaultVisibility_IsPrivate()
    {
        var repo = new GitRepository();
        Assert.Equal(GitRepoVisibility.Private, repo.Visibility);
    }

    [Fact]
    public void GitOrganization_Defaults()
    {
        var org = new GitOrganization();
        Assert.Equal(0, org.MemberCount);
        Assert.Equal(0, org.TeamCount);
    }

    [Fact]
    public void Team_DefaultPermission_IsRead()
    {
        var team = new Team();
        Assert.Equal(GitRole.Read, team.DefaultPermission);
    }

    [Fact]
    public void Comment_ParentTypes_AreValid()
    {
        var issueComment = new Comment { ParentType = nameof(Issue) };
        var prComment = new Comment { ParentType = nameof(PullRequest) };

        Assert.Equal("Issue", issueComment.ParentType);
        Assert.Equal("PullRequest", prComment.ParentType);
    }

    [Fact]
    public void MergeStrategy_Values()
    {
        Assert.Equal(0, (int)MergeStrategy.Merge);
        Assert.Equal(1, (int)MergeStrategy.Squash);
        Assert.Equal(2, (int)MergeStrategy.Rebase);
    }

    [Fact]
    public void GitRepoVisibility_Values()
    {
        Assert.Equal(0, (int)GitRepoVisibility.Private);
        Assert.Equal(1, (int)GitRepoVisibility.Internal);
        Assert.Equal(2, (int)GitRepoVisibility.Public);
    }
}

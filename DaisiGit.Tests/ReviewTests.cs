using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;

namespace DaisiGit.Tests;

public class ReviewTests
{
    [Fact]
    public void ReviewState_Ordering_IsCorrect()
    {
        Assert.Equal(0, (int)ReviewState.Commented);
        Assert.Equal(1, (int)ReviewState.Approved);
        Assert.Equal(2, (int)ReviewState.ChangesRequested);
        Assert.Equal(3, (int)ReviewState.Dismissed);
    }

    [Fact]
    public void DiffSide_Values()
    {
        Assert.Equal(0, (int)DiffSide.Left);
        Assert.Equal(1, (int)DiffSide.Right);
    }

    [Fact]
    public void Review_DefaultState_IsCommented()
    {
        var review = new Review();
        Assert.Equal(ReviewState.Commented, review.State);
    }

    [Fact]
    public void Review_Type_IsReview()
    {
        var review = new Review();
        Assert.Equal("Review", review.Type);
    }

    [Fact]
    public void DiffComment_Type_IsDiffComment()
    {
        var comment = new DiffComment();
        Assert.Equal("DiffComment", comment.Type);
    }

    [Fact]
    public void DiffComment_DefaultSide_IsRight()
    {
        var comment = new DiffComment();
        Assert.Equal(DiffSide.Right, comment.Side);
    }

    [Fact]
    public void DiffComment_Fields_HaveDefaults()
    {
        var comment = new DiffComment();
        Assert.Equal("", comment.Path);
        Assert.Equal(0, comment.Line);
        Assert.Equal("", comment.Body);
        Assert.Equal("", comment.AuthorId);
        Assert.Equal("", comment.AuthorName);
        Assert.Equal("", comment.ReviewId);
        Assert.Equal("", comment.RepositoryId);
    }

    [Fact]
    public void Review_Fields_HaveDefaults()
    {
        var review = new Review();
        Assert.Equal("", review.id);
        Assert.Equal("", review.RepositoryId);
        Assert.Equal("", review.PullRequestId);
        Assert.Equal(0, review.PullRequestNumber);
        Assert.Null(review.Body);
        Assert.Equal("", review.AuthorId);
        Assert.Equal("", review.AuthorName);
        Assert.Null(review.UpdatedUtc);
    }
}

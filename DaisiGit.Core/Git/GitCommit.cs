using System.Text;

namespace DaisiGit.Core.Git;

/// <summary>
/// A git commit object.
/// </summary>
public class GitCommit : GitObject
{
    public override GitObjectType Type => GitObjectType.Commit;
    public string TreeSha { get; set; } = "";
    public List<string> ParentShas { get; set; } = [];
    public GitSignature Author { get; set; } = new();
    public GitSignature Committer { get; set; } = new();
    public string Message { get; set; } = "";

    public override byte[] SerializeContent()
    {
        var sb = new StringBuilder();
        sb.Append($"tree {TreeSha}\n");
        foreach (var parent in ParentShas)
            sb.Append($"parent {parent}\n");
        sb.Append($"author {Author}\n");
        sb.Append($"committer {Committer}\n");
        sb.Append('\n');
        sb.Append(Message);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static GitCommit Parse(byte[] content)
    {
        var text = Encoding.UTF8.GetString(content);
        var commit = new GitCommit();

        var headerEnd = text.IndexOf("\n\n", StringComparison.Ordinal);
        var headers = text[..headerEnd];
        commit.Message = text[(headerEnd + 2)..];

        foreach (var line in headers.Split('\n'))
        {
            if (line.StartsWith("tree "))
                commit.TreeSha = line[5..];
            else if (line.StartsWith("parent "))
                commit.ParentShas.Add(line[7..]);
            else if (line.StartsWith("author "))
                commit.Author = GitSignature.Parse(line[7..]);
            else if (line.StartsWith("committer "))
                commit.Committer = GitSignature.Parse(line[10..]);
        }

        return commit;
    }
}

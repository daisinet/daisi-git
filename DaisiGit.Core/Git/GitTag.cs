using System.Text;

namespace DaisiGit.Core.Git;

/// <summary>
/// An annotated git tag object.
/// </summary>
public class GitTag : GitObject
{
    public override GitObjectType Type => GitObjectType.Tag;
    public string ObjectSha { get; set; } = "";
    public string ObjectType { get; set; } = "commit";
    public string TagName { get; set; } = "";
    public GitSignature Tagger { get; set; } = new();
    public string Message { get; set; } = "";

    public override byte[] SerializeContent()
    {
        var sb = new StringBuilder();
        sb.Append($"object {ObjectSha}\n");
        sb.Append($"type {ObjectType}\n");
        sb.Append($"tag {TagName}\n");
        sb.Append($"tagger {Tagger}\n");
        sb.Append('\n');
        sb.Append(Message);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static GitTag Parse(byte[] content)
    {
        var text = Encoding.UTF8.GetString(content);
        var tag = new GitTag();

        var headerEnd = text.IndexOf("\n\n", StringComparison.Ordinal);
        var headers = text[..headerEnd];
        tag.Message = text[(headerEnd + 2)..];

        foreach (var line in headers.Split('\n'))
        {
            if (line.StartsWith("object "))
                tag.ObjectSha = line[7..];
            else if (line.StartsWith("type "))
                tag.ObjectType = line[5..];
            else if (line.StartsWith("tag "))
                tag.TagName = line[4..];
            else if (line.StartsWith("tagger "))
                tag.Tagger = GitSignature.Parse(line[7..]);
        }

        return tag;
    }
}

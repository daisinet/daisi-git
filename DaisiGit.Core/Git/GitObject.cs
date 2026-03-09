using System.Text;

namespace DaisiGit.Core.Git;

/// <summary>
/// Base class for all git objects (blob, tree, commit, tag).
/// </summary>
public abstract class GitObject
{
    public string Sha { get; set; } = "";
    public abstract GitObjectType Type { get; }

    /// <summary>
    /// Serializes the object content (without the git header).
    /// </summary>
    public abstract byte[] SerializeContent();

    /// <summary>
    /// Serializes the full git object (header + content) ready for hashing/storage.
    /// </summary>
    public byte[] Serialize()
    {
        var content = SerializeContent();
        var header = Encoding.ASCII.GetBytes($"{TypeString} {content.Length}\0");
        var result = new byte[header.Length + content.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(content, 0, result, header.Length, content.Length);
        return result;
    }

    public string TypeString => Type switch
    {
        GitObjectType.Blob => "blob",
        GitObjectType.Tree => "tree",
        GitObjectType.Commit => "commit",
        GitObjectType.Tag => "tag",
        _ => throw new InvalidOperationException($"Unknown object type: {Type}")
    };

    public static GitObjectType ParseType(string type) => type switch
    {
        "blob" => GitObjectType.Blob,
        "tree" => GitObjectType.Tree,
        "commit" => GitObjectType.Commit,
        "tag" => GitObjectType.Tag,
        _ => throw new ArgumentException($"Unknown git object type: {type}")
    };
}

public enum GitObjectType
{
    Blob = 3,
    Tree = 2,
    Commit = 1,
    Tag = 4
}

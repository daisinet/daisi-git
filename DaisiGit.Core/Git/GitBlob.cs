namespace DaisiGit.Core.Git;

/// <summary>
/// A git blob — stores raw file content.
/// </summary>
public class GitBlob : GitObject
{
    public override GitObjectType Type => GitObjectType.Blob;
    public byte[] Data { get; set; } = [];

    public override byte[] SerializeContent() => Data;

    public static GitBlob Parse(byte[] content)
    {
        return new GitBlob { Data = content };
    }
}

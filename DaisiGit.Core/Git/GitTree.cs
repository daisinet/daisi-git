using System.Text;

namespace DaisiGit.Core.Git;

/// <summary>
/// A git tree — maps names to blobs and sub-trees.
/// </summary>
public class GitTree : GitObject
{
    public override GitObjectType Type => GitObjectType.Tree;
    public List<GitTreeEntry> Entries { get; set; } = [];

    public override byte[] SerializeContent()
    {
        using var ms = new MemoryStream();
        foreach (var entry in Entries.OrderBy(e => e.IsTree ? e.Name + "/" : e.Name))
        {
            var header = Encoding.ASCII.GetBytes($"{entry.Mode} {entry.Name}\0");
            ms.Write(header);
            ms.Write(ObjectHasher.ShaToBytes(entry.Sha));
        }
        return ms.ToArray();
    }

    public static GitTree Parse(byte[] content)
    {
        var tree = new GitTree();
        var i = 0;
        while (i < content.Length)
        {
            // Read mode (space-terminated)
            var spaceIdx = Array.IndexOf(content, (byte)' ', i);
            var mode = Encoding.ASCII.GetString(content, i, spaceIdx - i);
            i = spaceIdx + 1;

            // Read name (null-terminated)
            var nullIdx = Array.IndexOf(content, (byte)0, i);
            var name = Encoding.UTF8.GetString(content, i, nullIdx - i);
            i = nullIdx + 1;

            // Read 20-byte SHA
            var shaBytes = new byte[20];
            Buffer.BlockCopy(content, i, shaBytes, 0, 20);
            var sha = Convert.ToHexString(shaBytes).ToLowerInvariant();
            i += 20;

            tree.Entries.Add(new GitTreeEntry { Mode = mode, Name = name, Sha = sha });
        }
        return tree;
    }
}

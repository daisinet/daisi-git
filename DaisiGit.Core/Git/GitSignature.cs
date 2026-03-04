namespace DaisiGit.Core.Git;

/// <summary>
/// Author/committer signature with name, email, and timestamp.
/// </summary>
public class GitSignature
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public override string ToString()
    {
        var offset = Timestamp.Offset;
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var absOffset = offset.Duration();
        return $"{Name} <{Email}> {Timestamp.ToUnixTimeSeconds()} {sign}{absOffset.Hours:D2}{absOffset.Minutes:D2}";
    }

    public static GitSignature Parse(string line)
    {
        // Format: "Name <email> timestamp +0000"
        var ltIdx = line.IndexOf('<');
        var gtIdx = line.IndexOf('>');
        if (ltIdx < 0 || gtIdx < 0)
            throw new FormatException($"Invalid signature: {line}");

        var name = line[..ltIdx].TrimEnd();
        var email = line[(ltIdx + 1)..gtIdx];
        var rest = line[(gtIdx + 2)..].Trim().Split(' ');

        var unixTime = long.Parse(rest[0]);
        var tzString = rest[1];
        var tzSign = tzString[0] == '+' ? 1 : -1;
        var tzHours = int.Parse(tzString[1..3]);
        var tzMinutes = int.Parse(tzString[3..5]);
        var offset = new TimeSpan(tzSign * tzHours, tzSign * tzMinutes, 0);

        return new GitSignature
        {
            Name = name,
            Email = email,
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(unixTime).ToOffset(offset)
        };
    }
}

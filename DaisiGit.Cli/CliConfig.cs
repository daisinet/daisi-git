using System.Text.Json;

namespace DaisiGit.Cli;

/// <summary>
/// Manages CLI configuration stored at ~/.daisigit/config.json
/// </summary>
public class CliConfig
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daisigit");
    private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

    public string? ServerUrl { get; set; }
    public string? SessionToken { get; set; }
    public string? UserName { get; set; }

    public static CliConfig Load()
    {
        if (!File.Exists(ConfigFile))
            return new CliConfig();

        var json = File.ReadAllText(ConfigFile);
        return JsonSerializer.Deserialize<CliConfig>(json) ?? new CliConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigFile, json);
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(ServerUrl) && !string.IsNullOrEmpty(SessionToken);
}

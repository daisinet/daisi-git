namespace DaisiGit.IntegrationTests;

/// <summary>
/// Integration test configuration.
/// Set DAISIGIT_API_KEY env var to a valid personal access token (dg_...).
/// </summary>
public static class TestConfig
{
    public static string ServerUrl =>
        Environment.GetEnvironmentVariable("DAISIGIT_SERVER_URL") ?? "https://localhost:5003";

    public static string ApiKey =>
        Environment.GetEnvironmentVariable("DAISIGIT_API_KEY") ?? "";

    public static bool IsConfigured => !string.IsNullOrEmpty(ApiKey) && ApiKey.StartsWith("dg_");
}

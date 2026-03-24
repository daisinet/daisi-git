using DaisiGit.Core.Enums;
using System.Text.Json.Serialization;

namespace DaisiGit.Core.Models;

/// <summary>
/// Per-account settings stored in Cosmos DB, partitioned by AccountId.
/// Controls account-level defaults such as the storage provider for new repositories.
/// </summary>
public class AccountSettings
{
    public string id { get; set; } = "";
    public string Type { get; set; } = nameof(AccountSettings);
    public string AccountId { get; set; } = "";

    /// <summary>
    /// Default storage provider for new repositories created under this account.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StorageProvider DefaultStorageProvider { get; set; } = StorageProvider.DaisiDrive;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedUtc { get; set; }
}

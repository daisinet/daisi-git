using DaisiGit.Core.Enums;
using DaisiGit.Core.Models;

namespace DaisiGit.Tests;

public class StorageProviderTests
{
    [Fact]
    public void StorageProvider_HasExpectedValues()
    {
        Assert.Equal(0, (int)StorageProvider.DaisiDrive);
        Assert.Equal(1, (int)StorageProvider.AzureBlob);
    }

    [Fact]
    public void GitRepository_StorageProvider_DefaultsToNull()
    {
        var repo = new GitRepository();
        Assert.Null(repo.StorageProvider);
    }

    [Fact]
    public void GitRepository_StorageProvider_CanBeSet()
    {
        var repo = new GitRepository { StorageProvider = StorageProvider.AzureBlob };
        Assert.Equal(StorageProvider.AzureBlob, repo.StorageProvider);
    }

    [Fact]
    public void GitObjectRecord_StorageProvider_DefaultsToDaisiDrive()
    {
        var record = new GitObjectRecord();
        Assert.Equal(StorageProvider.DaisiDrive, record.StorageProvider);
    }

    [Fact]
    public void GitObjectRecord_StorageProvider_CanBeSet()
    {
        var record = new GitObjectRecord { StorageProvider = StorageProvider.AzureBlob };
        Assert.Equal(StorageProvider.AzureBlob, record.StorageProvider);
    }

    [Fact]
    public void AccountSettings_DefaultStorageProvider_DefaultsToDaisiDrive()
    {
        var settings = new AccountSettings();
        Assert.Equal(StorageProvider.DaisiDrive, settings.DefaultStorageProvider);
    }

    [Fact]
    public void AccountSettings_DefaultStorageProvider_CanBeSet()
    {
        var settings = new AccountSettings
        {
            AccountId = "acct-123",
            DefaultStorageProvider = StorageProvider.AzureBlob
        };

        Assert.Equal("acct-123", settings.AccountId);
        Assert.Equal(StorageProvider.AzureBlob, settings.DefaultStorageProvider);
    }

    [Fact]
    public void AccountSettings_Type_IsAccountSettings()
    {
        var settings = new AccountSettings();
        Assert.Equal("AccountSettings", settings.Type);
    }
}

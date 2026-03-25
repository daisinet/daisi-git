using System.Security.Cryptography;
using System.Text;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Manages per-repository encrypted secrets for workflow execution.
/// Secrets are encrypted with AES-256 using a server-side key.
/// Values are never exposed via API — only names are listed.
/// </summary>
public class SecretService(DaisiGitCosmo cosmo, string encryptionKey)
{
    /// <summary>
    /// Sets a secret. Creates or updates.
    /// </summary>
    public async Task SetSecretAsync(string repositoryId, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Secret name is required.");

        var encrypted = Encrypt(value);

        await cosmo.UpsertSecretAsync(new RepoSecret
        {
            RepositoryId = repositoryId,
            Name = name.Trim().ToUpperInvariant(),
            EncryptedValue = encrypted
        });
    }

    /// <summary>
    /// Gets the decrypted value of a secret. Used internally by the workflow engine.
    /// </summary>
    public async Task<string?> GetSecretValueAsync(string repositoryId, string name)
    {
        var secret = await cosmo.GetSecretAsync(repositoryId, name.ToUpperInvariant());
        if (secret == null) return null;
        return Decrypt(secret.EncryptedValue);
    }

    /// <summary>
    /// Lists secret names (not values) for a repository.
    /// </summary>
    public async Task<List<string>> ListSecretNamesAsync(string repositoryId)
    {
        var secrets = await cosmo.GetSecretsAsync(repositoryId);
        return secrets.Select(s => s.Name).ToList();
    }

    /// <summary>
    /// Deletes a secret.
    /// </summary>
    public async Task DeleteSecretAsync(string repositoryId, string name)
    {
        await cosmo.DeleteSecretAsync(repositoryId, name.ToUpperInvariant());
    }

    // ── Org-level secrets ──

    private static string OrgPartitionKey(string orgId) => $"org:{orgId}";

    public async Task SetOrgSecretAsync(string organizationId, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Secret name is required.");

        await cosmo.UpsertSecretAsync(new RepoSecret
        {
            RepositoryId = OrgPartitionKey(organizationId),
            OrganizationId = organizationId,
            Name = name.Trim().ToUpperInvariant(),
            EncryptedValue = Encrypt(value)
        });
    }

    public async Task<List<string>> ListOrgSecretNamesAsync(string organizationId)
    {
        var secrets = await cosmo.GetSecretsAsync(OrgPartitionKey(organizationId));
        return secrets.Select(s => s.Name).ToList();
    }

    public async Task DeleteOrgSecretAsync(string organizationId, string name)
    {
        await cosmo.DeleteSecretAsync(OrgPartitionKey(organizationId), name.ToUpperInvariant());
    }

    /// <summary>
    /// Resolves all secrets for a repository into a dictionary for workflow execution.
    /// Org secrets are loaded first, then repo secrets override.
    /// Keys are "secrets.NAME", values are decrypted.
    /// </summary>
    public async Task<Dictionary<string, string>> ResolveSecretsAsync(string repositoryId, string? organizationId = null)
    {
        var result = new Dictionary<string, string>();

        // Org-level secrets first (lower priority)
        if (!string.IsNullOrEmpty(organizationId))
        {
            var orgSecrets = await cosmo.GetSecretsAsync(OrgPartitionKey(organizationId));
            foreach (var s in orgSecrets)
            {
                try { result[$"secrets.{s.Name}"] = Decrypt(s.EncryptedValue); } catch { }
            }
        }

        // Repo-level secrets override
        var repoSecrets = await cosmo.GetSecretsAsync(repositoryId);
        foreach (var s in repoSecrets)
        {
            try { result[$"secrets.{s.Name}"] = Decrypt(s.EncryptedValue); } catch { }
        }

        return result;
    }

    // ── AES-256-CBC encryption ──

    private string Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKey();
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Prepend IV to ciphertext
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    private string Decrypt(string ciphertext)
    {
        var data = Convert.FromBase64String(ciphertext);

        using var aes = Aes.Create();
        aes.Key = DeriveKey();

        var iv = new byte[16];
        Buffer.BlockCopy(data, 0, iv, 0, 16);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var cipherBytes = new byte[data.Length - 16];
        Buffer.BlockCopy(data, 16, cipherBytes, 0, cipherBytes.Length);

        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private byte[] DeriveKey()
    {
        // Derive a 256-bit key from the encryption key string
        return SHA256.HashData(Encoding.UTF8.GetBytes(encryptionKey));
    }
}

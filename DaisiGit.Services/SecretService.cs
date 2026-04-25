using System.Security.Cryptography;
using System.Text;
using DaisiGit.Core.Models;
using DaisiGit.Data;

namespace DaisiGit.Services;

/// <summary>
/// Manages encrypted secrets for workflows.
/// Secrets can be scoped to a repo or org. Org secrets are inherited by all repos.
/// Repo-level secrets override org-level secrets with the same name.
/// </summary>
public class SecretService(DaisiGitCosmo cosmo, string encryptionKey)
{
    /// <summary>Well-known OwnerId for system-scope secrets (engine-owned credentials, not user-managed).</summary>
    public const string SystemOwnerId = "_system";

    // ── Repo-level ──

    public async Task SetSecretAsync(string repositoryId, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Secret name is required.");

        await cosmo.UpsertSecretAsync(new RepoSecret
        {
            OwnerId = repositoryId,
            Scope = "repo",
            Name = name.Trim().ToUpperInvariant(),
            EncryptedValue = Encrypt(value)
        });
    }

    public async Task<List<string>> ListSecretNamesAsync(string repositoryId)
    {
        var secrets = await cosmo.GetSecretsAsync(repositoryId);
        return secrets.Select(s => s.Name).ToList();
    }

    public async Task DeleteSecretAsync(string repositoryId, string name)
    {
        await cosmo.DeleteSecretAsync(repositoryId, name.ToUpperInvariant());
    }

    // ── Org-level ──

    public async Task SetOrgSecretAsync(string organizationId, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Secret name is required.");

        await cosmo.UpsertSecretAsync(new RepoSecret
        {
            OwnerId = organizationId,
            Scope = "org",
            Name = name.Trim().ToUpperInvariant(),
            EncryptedValue = Encrypt(value)
        });
    }

    public async Task<List<string>> ListOrgSecretNamesAsync(string organizationId)
    {
        var secrets = await cosmo.GetSecretsAsync(organizationId);
        return secrets.Select(s => s.Name).ToList();
    }

    public async Task DeleteOrgSecretAsync(string organizationId, string name)
    {
        await cosmo.DeleteSecretAsync(organizationId, name.ToUpperInvariant());
    }

    // ── Resolution (org + repo merged) ──

    /// <summary>
    /// Resolves all secrets for a repository. Org secrets loaded first, repo overrides.
    /// </summary>
    public async Task<Dictionary<string, string>> ResolveSecretsAsync(string repositoryId, string? organizationId = null)
    {
        var result = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(organizationId))
        {
            var orgSecrets = await cosmo.GetSecretsAsync(organizationId);
            foreach (var s in orgSecrets)
                try { result[$"secrets.{s.Name}"] = Decrypt(s.EncryptedValue); } catch { }
        }

        var repoSecrets = await cosmo.GetSecretsAsync(repositoryId);
        foreach (var s in repoSecrets)
            try { result[$"secrets.{s.Name}"] = Decrypt(s.EncryptedValue); } catch { }

        return result;
    }

    public async Task<string?> GetSecretValueAsync(string repositoryId, string name)
    {
        var secret = await cosmo.GetSecretAsync(repositoryId, name.ToUpperInvariant());
        if (secret == null) return null;
        return Decrypt(secret.EncryptedValue);
    }

    // ── System-level (engine-owned credentials, not user-managed) ──

    /// <summary>
    /// Store a system-scope secret. Use for engine-owned credentials like the DaisiGit Workers
    /// ORC SECRET-KEY. Not surfaced in repo/org UIs.
    /// </summary>
    public async Task SetSystemSecretAsync(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Secret name is required.");

        await cosmo.UpsertSecretAsync(new RepoSecret
        {
            OwnerId = SystemOwnerId,
            Scope = "system",
            Name = name.Trim().ToUpperInvariant(),
            EncryptedValue = Encrypt(value)
        });
    }

    /// <summary>Resolve a single system-scope secret by name. Returns null if unset.</summary>
    public async Task<string?> ResolveSystemSecretAsync(string name)
    {
        var secret = await cosmo.GetSecretAsync(SystemOwnerId, name.Trim().ToUpperInvariant());
        if (secret == null) return null;
        try { return Decrypt(secret.EncryptedValue); }
        catch { return null; }
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
        return SHA256.HashData(Encoding.UTF8.GetBytes(encryptionKey));
    }
}

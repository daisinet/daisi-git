using System.Diagnostics;
using System.Text;
using DaisiGit.Core.Enums;
using DaisiGit.Core.Git;
using DaisiGit.Core.Git.Pack;
using DaisiGit.Core.Git.Protocol;
using DaisiGit.Core.Models;
using DaisiGit.Services;

namespace DaisiGit.Web.Endpoints;

/// <summary>
/// HTTP smart protocol endpoints for git clone/fetch/push.
/// </summary>
public static class GitSmartProtocolEndpoints
{
    public static void MapGitSmartProtocolEndpoints(this WebApplication app)
    {
        // Ref advertisement (used by both fetch and push)
        app.MapGet("/{owner}/{repo}.git/info/refs", HandleInfoRefs);

        // Upload-pack (fetch/clone)
        app.MapPost("/{owner}/{repo}.git/git-upload-pack", HandleUploadPack);

        // Receive-pack (push)
        app.MapPost("/{owner}/{repo}.git/git-receive-pack", HandleReceivePack);
    }

    /// <summary>
    /// GET /owner/repo.git/info/refs?service=git-upload-pack|git-receive-pack
    /// Advertises refs for clone/fetch/push.
    /// </summary>
    private static async Task HandleInfoRefs(
        HttpContext ctx,
        string owner, string repo,
        string? service,
        RepositoryService repoService,
        GitRefService refService,
        PermissionService permissionService)
    {
        if (string.IsNullOrEmpty(service) || (service != "git-upload-pack" && service != "git-receive-pack"))
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsync("Invalid service");
            return;
        }

        var repository = await repoService.GetRepositoryBySlugAsync(owner, repo);
        if (repository == null)
        {
            ctx.Response.StatusCode = 404;
            await ctx.Response.WriteAsync("Repository not found");
            return;
        }

        // Permission check
        var userId = ctx.Items["userId"] as string ?? "";
        if (service == "git-receive-pack")
        {
            if (!await permissionService.CanWriteAsync(userId, repository))
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync("Permission denied");
                return;
            }
        }
        else
        {
            if (!await permissionService.CanReadAsync(userId, repository))
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync("Permission denied");
                return;
            }
        }

        ctx.Response.ContentType = $"application/x-{service}-advertisement";
        ctx.Response.Headers["Cache-Control"] = "no-cache";

        var stream = ctx.Response.Body;

        // Write service announcement
        var serviceAnnounce = PktLine.Encode($"# service={service}");
        await stream.WriteAsync(serviceAnnounce);
        await stream.WriteAsync(PktLine.Flush);

        // Get all refs
        var refs = await refService.GetAllRefsAsync(repository.id);
        var headSha = await refService.ResolveHeadAsync(repository.id);

        if (refs.Count == 0 && headSha == null)
        {
            // Empty repository — advertise capabilities with zero-id
            var zeroId = new string('0', 40);
            var capLine = $"{zeroId} capabilities^{{}}\0 report-status delete-refs side-band-64k ofs-delta";
            await stream.WriteAsync(PktLine.Encode(capLine));
            await stream.WriteAsync(PktLine.Flush);
            return;
        }

        var capabilities = " report-status delete-refs ofs-delta";
        var first = true;

        // HEAD first
        if (headSha != null)
        {
            var line = first
                ? $"{headSha} HEAD\0{capabilities}"
                : $"{headSha} HEAD";
            await stream.WriteAsync(PktLine.Encode(line));
            first = false;
        }

        // Then all refs sorted alphabetically
        foreach (var (refName, sha) in refs.OrderBy(r => r.Key))
        {
            var line = first
                ? $"{sha} {refName}\0{capabilities}"
                : $"{sha} {refName}";
            await stream.WriteAsync(PktLine.Encode(line));
            first = false;
        }

        await stream.WriteAsync(PktLine.Flush);
    }

    /// <summary>
    /// POST /owner/repo.git/git-upload-pack
    /// Handles fetch/clone — sends requested objects as a pack file.
    /// </summary>
    private static async Task HandleUploadPack(
        HttpContext ctx,
        string owner, string repo,
        RepositoryService repoService,
        GitRefService refService,
        GitObjectStore objectStore,
        PermissionService permissionService)
    {
        var repository = await repoService.GetRepositoryBySlugAsync(owner, repo);
        if (repository == null)
        {
            ctx.Response.StatusCode = 404;
            return;
        }

        var userId = ctx.Items["userId"] as string ?? "";
        if (!await permissionService.CanReadAsync(userId, repository))
        {
            ctx.Response.StatusCode = 403;
            return;
        }

        ctx.Response.ContentType = "application/x-git-upload-pack-result";

        // Read client wants/haves
        var lines = await PktLine.ReadAllLinesAsync(ctx.Request.Body);

        var wants = new List<string>();
        var haves = new HashSet<string>();
        var done = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("want "))
            {
                var sha = line.Split(' ')[1];
                wants.Add(sha);
            }
            else if (line.StartsWith("have "))
            {
                haves.Add(line[5..].Trim());
            }
            else if (line == "done")
            {
                done = true;
            }
        }

        if (wants.Count == 0)
        {
            await ctx.Response.Body.WriteAsync(PktLine.Encode("NAK"));
            await ctx.Response.Body.WriteAsync(PktLine.Flush);
            return;
        }

        // Send NAK (we don't do multi-ack)
        await ctx.Response.Body.WriteAsync(PktLine.Encode("NAK"));

        // Walk the object graph from wants, excluding haves
        var objectsToSend = await CollectObjectsAsync(repository.id, wants, haves, objectStore);

        // Generate pack file
        var packData = PackFile.Generate(objectsToSend);

        // Send pack data directly (no sideband framing — simpler and widely compatible)
        await ctx.Response.Body.WriteAsync(packData);
        await ctx.Response.Body.FlushAsync();
    }

    /// <summary>
    /// POST /owner/repo.git/git-receive-pack
    /// Handles push — receives a pack file and updates refs.
    /// </summary>
    private static async Task HandleReceivePack(
        HttpContext ctx,
        string owner, string repo,
        RepositoryService repoService,
        GitRefService refService,
        GitObjectStore objectStore,
        PermissionService permissionService,
        GitEventService events)
    {
        var repository = await repoService.GetRepositoryBySlugAsync(owner, repo);
        if (repository == null)
        {
            ctx.Response.StatusCode = 404;
            return;
        }

        var userId = ctx.Items["userId"] as string ?? "";
        if (!await permissionService.CanWriteAsync(userId, repository))
        {
            ctx.Response.StatusCode = 403;
            return;
        }

        ctx.Response.ContentType = "application/x-git-receive-pack-result";

        // Read ref update commands
        var lines = await PktLine.ReadAllLinesAsync(ctx.Request.Body);
        var refUpdates = new List<(string oldSha, string newSha, string refName)>();

        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line)) continue;
            // Format: "old-sha new-sha refs/heads/branch\0capabilities"
            var cleanLine = line.Contains('\0') ? line[..line.IndexOf('\0')] : line;
            var parts = cleanLine.Split(' ', 3);
            if (parts.Length == 3 && parts[0].Length == 40)
            {
                refUpdates.Add((parts[0], parts[1], parts[2]));
            }
        }

        // Read pack data (rest of the stream)
        var packData = await ReadRemainingAsync(ctx.Request.Body);

        // Parse and store objects from pack
        if (packData.Length > 0)
        {
            // The pack data may need to be trimmed to start at "PACK" header
            var packStart = 0;
            for (var i = 0; i <= packData.Length - 4; i++)
            {
                if (packData[i] == 'P' && packData[i + 1] == 'A' && packData[i + 2] == 'C' && packData[i + 3] == 'K')
                {
                    packStart = i;
                    break;
                }
            }
            if (packStart > 0)
                packData = packData[packStart..];

            await UnpackWithGitAsync(repository, packData, objectStore);
        }

        // Apply ref updates
        var results = new List<string>();
        foreach (var (oldSha, newSha, refName) in refUpdates)
        {
            var isDelete = newSha == new string('0', 40);
            if (isDelete)
            {
                await refService.DeleteRefAsync(repository.id, refName);
                results.Add($"ok {refName}");
            }
            else
            {
                var success = await refService.UpdateRefAsync(repository.id, refName, newSha,
                    oldSha == new string('0', 40) ? null : oldSha);

                results.Add(success ? $"ok {refName}" : $"ng {refName} non-fast-forward");
            }
        }

        // Mark repo as non-empty if it was
        if (repository.IsEmpty && refUpdates.Count > 0)
        {
            repository.IsEmpty = false;
            await repoService.UpdateRepositoryAsync(repository);
        }

        // Emit events for each successful ref update
        var zeroSha = new string('0', 40);
        var userName = ctx.Items["userName"] as string ?? "";
        foreach (var (oldSha, newSha, refName) in refUpdates)
        {
            var isCreate = oldSha == zeroSha;
            var isDelete = newSha == zeroSha;
            var isBranch = refName.StartsWith("refs/heads/");
            var isTag = refName.StartsWith("refs/tags/");
            var shortName = refName.Replace("refs/heads/", "").Replace("refs/tags/", "");

            var eventType = (isBranch, isCreate, isDelete) switch
            {
                (true, true, false) => GitTriggerType.BranchCreated,
                (true, false, true) => GitTriggerType.BranchDeleted,
                (true, false, false) => GitTriggerType.PushToRef,
                _ when isTag && isCreate => GitTriggerType.TagCreated,
                _ when isTag && isDelete => GitTriggerType.TagDeleted,
                _ => GitTriggerType.PushToRef
            };

            var payload = new Dictionary<string, string>
            {
                ["push.ref"] = refName,
                ["push.branch"] = isBranch ? shortName : "",
                ["push.tag"] = isTag ? shortName : "",
                ["push.oldSha"] = oldSha,
                ["push.newSha"] = newSha,
                ["push.isCreate"] = isCreate.ToString(),
                ["push.isDelete"] = isDelete.ToString()
            };

            await events.EmitAsync(repository.AccountId, repository.id, eventType,
                userId, userName, payload);
        }

        // Send report-status as plain pkt-lines (not sideband)
        await ctx.Response.Body.WriteAsync(PktLine.Encode("unpack ok"));
        foreach (var line in results)
        {
            await ctx.Response.Body.WriteAsync(PktLine.Encode(line));
        }

        await ctx.Response.Body.WriteAsync(PktLine.Flush);
    }

    /// <summary>
    /// Walks the object graph starting from want SHAs, collecting all objects not in haves.
    /// </summary>
    private static async Task<List<GitObject>> CollectObjectsAsync(
        string repositoryId, List<string> wants, HashSet<string> haves, GitObjectStore objectStore)
    {
        var objects = new List<GitObject>();
        var visited = new HashSet<string>(haves);
        var queue = new Queue<string>(wants);

        while (queue.Count > 0)
        {
            var sha = queue.Dequeue();
            if (!visited.Add(sha))
                continue;

            var obj = await objectStore.GetObjectAsync(repositoryId, sha);
            if (obj == null)
                continue;

            objects.Add(obj);

            switch (obj)
            {
                case GitCommit commit:
                    queue.Enqueue(commit.TreeSha);
                    foreach (var parent in commit.ParentShas)
                        queue.Enqueue(parent);
                    break;
                case GitTree tree:
                    foreach (var entry in tree.Entries)
                        queue.Enqueue(entry.Sha);
                    break;
                case GitTag tag:
                    queue.Enqueue(tag.ObjectSha);
                    break;
            }
        }

        return objects;
    }

    /// <summary>
    /// Sends pack data using sideband-64k framing.
    /// </summary>
    private static async Task SendSidebandPackAsync(Stream output, byte[] packData)
    {
        const int maxChunkSize = 65515; // 65519 - 4 (pkt-line header)
        var offset = 0;

        while (offset < packData.Length)
        {
            var chunkSize = Math.Min(maxChunkSize - 1, packData.Length - offset); // -1 for sideband byte
            var sideband = new byte[chunkSize + 1];
            sideband[0] = 1; // sideband channel 1 = pack data
            Buffer.BlockCopy(packData, offset, sideband, 1, chunkSize);
            await output.WriteAsync(PktLine.EncodeRaw(sideband));
            offset += chunkSize;
        }
    }

    /// <summary>
    /// Unpacks a pack file using the git binary, then stores each object individually.
    /// This avoids .NET's zlib boundary detection issues by delegating to git's native zlib.
    /// </summary>
    private static async Task UnpackWithGitAsync(
        GitRepository repository, byte[] packData, GitObjectStore objectStore)
    {
        // Create a temp bare repo, feed the pack to git unpack-objects, then read each object
        var tempDir = Path.Combine(Path.GetTempPath(), $"daisigit-unpack-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            // Initialize bare repo
            await RunGitAsync(tempDir, "init --bare");

            // Feed pack data to git unpack-objects
            var unpack = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "unpack-objects -q",
                    WorkingDirectory = tempDir,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            unpack.Start();
            await unpack.StandardInput.BaseStream.WriteAsync(packData);
            unpack.StandardInput.Close();
            await unpack.WaitForExitAsync();

            var unpackErr = await unpack.StandardError.ReadToEndAsync();
            if (unpack.ExitCode != 0)
                throw new InvalidOperationException(
                    $"git unpack-objects failed (exit {unpack.ExitCode}): {unpackErr}");

            // List all objects git unpacked
            var listResult = await RunGitAsync(tempDir,
                "cat-file --batch-check=%(objectname) %(objecttype) %(objectsize) --batch-all-objects");

            foreach (var line in listResult.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim().Split(' ');
                if (parts.Length < 3) continue;

                var sha = parts[0];
                var objectType = parts[1];
                var size = int.Parse(parts[2]);

                // Read the loose object file directly — git stores unpacked objects as
                // zlib-compressed files at objects/{sha[0:2]}/{sha[2:]}
                var loosePath = Path.Combine(tempDir, "objects", sha[..2], sha[2..]);
                if (File.Exists(loosePath))
                {
                    // The loose object is already zlib-compressed with the git header.
                    // Decompress to get the raw object, then re-compress with our format.
                    var compressed = await File.ReadAllBytesAsync(loosePath);
                    var raw = ObjectHasher.ZlibDecompress(compressed);
                    await objectStore.StoreRawObjectAsync(repository, raw, objectType);
                }
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static async Task<string> RunGitAsync(string workDir, string args)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = workDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }

    /// <summary>
    /// Stores all entries from a parsed pack file.
    /// </summary>
    private static async Task StorePackEntriesAsync(
        GitRepository repository,
        List<PackEntry> entries, GitObjectStore objectStore)
    {
        // First pass: store non-delta objects and build SHA map
        var shaMap = new Dictionary<int, PackEntry>(); // offset → entry (for OFS_DELTA)
        var resolved = new Dictionary<string, byte[]>(); // sha → raw content

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.DeltaData == null)
            {
                // Non-delta object — store directly
                var typeStr = entry.ObjectType switch
                {
                    GitObjectType.Commit => "commit",
                    GitObjectType.Tree => "tree",
                    GitObjectType.Blob => "blob",
                    GitObjectType.Tag => "tag",
                    _ => "blob"
                };
                var header = Encoding.ASCII.GetBytes($"{typeStr} {entry.Data.Length}\0");
                var raw = new byte[header.Length + entry.Data.Length];
                Buffer.BlockCopy(header, 0, raw, 0, header.Length);
                Buffer.BlockCopy(entry.Data, 0, raw, header.Length, entry.Data.Length);

                entry.Sha = ObjectHasher.HashRaw(raw);
                resolved[entry.Sha] = entry.Data;
                await objectStore.StoreRawObjectAsync(repository, raw, typeStr);
            }
        }

        // Second pass: resolve deltas
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.DeltaData != null)
            {
                byte[]? baseData = null;

                if (entry.BaseSha != null && resolved.TryGetValue(entry.BaseSha, out var data))
                {
                    baseData = data;
                }
                else if (entry.BaseSha != null)
                {
                    // Try to fetch from store
                    var baseObj = await objectStore.GetObjectAsync(repository.id, entry.BaseSha);
                    if (baseObj != null)
                        baseData = baseObj.SerializeContent();
                }

                if (baseData != null)
                {
                    var result = PackFile.ApplyDelta(baseData, entry.DeltaData);
                    // Determine type from base object
                    var typeStr = "blob"; // Default
                    if (entry.BaseSha != null)
                    {
                        var baseRaw = await objectStore.GetRawObjectAsync(repository.id, entry.BaseSha);
                        if (baseRaw != null)
                        {
                            var (baseType, _, _) = ObjectHasher.ParseRawObject(baseRaw);
                            typeStr = baseType;
                        }
                    }

                    var header = Encoding.ASCII.GetBytes($"{typeStr} {result.Length}\0");
                    var raw = new byte[header.Length + result.Length];
                    Buffer.BlockCopy(header, 0, raw, 0, header.Length);
                    Buffer.BlockCopy(result, 0, raw, header.Length, result.Length);

                    entry.Sha = ObjectHasher.HashRaw(raw);
                    resolved[entry.Sha] = result;
                    await objectStore.StoreRawObjectAsync(repository, raw, typeStr);
                }
            }
        }
    }

    private static async Task<byte[]> ReadRemainingAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
        {
            ms.Write(buffer, 0, bytesRead);
        }
        return ms.ToArray();
    }
}

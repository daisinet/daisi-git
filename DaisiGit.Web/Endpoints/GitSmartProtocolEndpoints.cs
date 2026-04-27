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
            var emptyCapabilities = service == "git-upload-pack"
                ? " report-status delete-refs side-band-64k ofs-delta"
                : " report-status delete-refs ofs-delta";
            var capLine = $"{zeroId} capabilities^{{}}\0{emptyCapabilities}";
            await stream.WriteAsync(PktLine.Encode(capLine));
            await stream.WriteAsync(PktLine.Flush);
            return;
        }

        var capabilities = service == "git-upload-pack"
            ? " report-status delete-refs side-band-64k ofs-delta"
            : " report-status delete-refs ofs-delta";
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

        // Read client wants/haves — client sends wants, flush, then haves+done, flush.
        // We need to read across the first flush to get done.
        var lines = await PktLine.ReadAllLinesAsync(ctx.Request.Body);
        var moreLines = await PktLine.ReadAllLinesAsync(ctx.Request.Body);
        lines.AddRange(moreLines);

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

        // Send pack data via sideband-64k framing
        await SendSidebandPackAsync(ctx.Response.Body, packData);
        await ctx.Response.Body.WriteAsync(PktLine.Flush);
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
        BrowseService browseService,
        PermissionService permissionService,
        GitEventService events,
        RepoActivityRollupService rollupService)
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

        // Read the ENTIRE request body first, then parse pkt-lines and pack data from it.
        // This avoids stream read-ahead issues where ReadAllLinesAsync consumes pack bytes.
        var fullBody = await ReadRemainingAsync(ctx.Request.Body);

        // Find the PACK header in the body — everything before it is pkt-line ref commands
        var packStart = -1;
        for (var i = 0; i <= fullBody.Length - 4; i++)
        {
            if (fullBody[i] == 'P' && fullBody[i + 1] == 'A' && fullBody[i + 2] == 'C' && fullBody[i + 3] == 'K')
            {
                packStart = i;
                break;
            }
        }

        // Parse ref commands from the pkt-line portion
        var refUpdates = new List<(string oldSha, string newSha, string refName)>();
        if (packStart > 0)
        {
            using var refStream = new MemoryStream(fullBody, 0, packStart);
            var lines = await PktLine.ReadAllLinesAsync(refStream);
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                var cleanLine = line.Contains('\0') ? line[..line.IndexOf('\0')] : line;
                var parts = cleanLine.Split(' ', 3);
                if (parts.Length == 3 && parts[0].Length == 40)
                    refUpdates.Add((parts[0], parts[1], parts[2]));
            }
        }

        // Extract pack data
        var packData = packStart >= 0 ? fullBody[packStart..] : Array.Empty<byte>();

        // Parse and store objects from pack
        if (packData.Length > 0)
        {
            var entries = PackFile.Parse(packData);
            await StorePackEntriesAsync(repository, entries, objectStore);
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

            // Compute the changed-files list for branch updates (not deletes/tags) so the
            // path-based trigger filter has something to match. Best-effort — failures
            // here just leave the filter unmatched.
            if (isBranch && !isDelete)
            {
                try
                {
                    var changed = await browseService.GetChangedPathsAsync(repository.id, oldSha, newSha);
                    if (changed.Count > 0)
                        payload["push.changedPaths"] = string.Join("\n", changed);
                }
                catch { }
            }

            await events.EmitAsync(repository.AccountId, repository.id, eventType,
                userId, userName, payload);

            // Bump the per-day commit rollup. Only for branch updates (not tags/deletes);
            // we want the count to reflect commits landing on a branch.
            if (isBranch && !isDelete && eventType != GitTriggerType.BranchDeleted)
            {
                try { await rollupService.ApplyPushAsync(repository, oldSha, newSha); }
                catch { /* rollup failure should never break a push */ }
            }
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
            {
                Console.WriteLine($"[CollectObjects] MISSING object: {sha}");
                continue;
            }

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
        GitRepository repository, byte[] packData, GitObjectStore objectStore, GitRefService refService)
    {
        // Create a temp bare repo, feed the pack to git unpack-objects, then read each object
        var tempDir = Path.Combine(Path.GetTempPath(), $"daisigit-unpack-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            // Initialize bare repo
            await RunGitAsync(tempDir, "init --bare");

            // Export existing objects from our store into the temp repo so that
            // delta-compressed objects in thin packs can resolve their base objects.
            var existingRefs = await refService.GetAllRefsAsync(repository.id);
            var existingObjects = new HashSet<string>();
            foreach (var kvp in existingRefs)
            {
                await ExportObjectGraphAsync(repository.id, kvp.Value, tempDir, objectStore, existingObjects);
            }

            // Feed the pack via stdin to index-pack --fix-thin which resolves deltas
            var indexPack = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "index-pack --fix-thin --stdin",
                    WorkingDirectory = tempDir,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            indexPack.Start();
            await indexPack.StandardInput.BaseStream.WriteAsync(packData);
            indexPack.StandardInput.Close();
            var indexOut = await indexPack.StandardOutput.ReadToEndAsync();
            var indexErr = await indexPack.StandardError.ReadToEndAsync();
            await indexPack.WaitForExitAsync();

            if (indexPack.ExitCode != 0)
                throw new InvalidOperationException(
                    $"git index-pack failed (exit {indexPack.ExitCode}): {indexErr}");

            // List all objects git unpacked
            var listResult = await RunGitAsync(tempDir,
                "cat-file --batch-check=%(objectname) %(objecttype) %(objectsize) --batch-all-objects");

            foreach (var line in listResult.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim().Split(' ');
                if (parts.Length < 3) continue;

                var sha = parts[0];
                var objectType = parts[1];

                // Read the object content via git cat-file (binary-safe via raw stream)
                var catFile = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = $"cat-file -p {sha}",
                        WorkingDirectory = tempDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                catFile.Start();
                using var contentMs = new MemoryStream();
                await catFile.StandardOutput.BaseStream.CopyToAsync(contentMs);
                await catFile.WaitForExitAsync();
                var content = contentMs.ToArray();

                // Build the raw git object (type + space + size + null + content)
                var header = Encoding.ASCII.GetBytes($"{objectType} {content.Length}\0");
                var raw = new byte[header.Length + content.Length];
                Buffer.BlockCopy(header, 0, raw, 0, header.Length);
                Buffer.BlockCopy(content, 0, raw, header.Length, content.Length);

                await objectStore.StoreRawObjectAsync(repository, raw, objectType);
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// Exports an object and its reachable graph from our store into a bare git repo's
    /// object directory (as loose objects). Used to populate base objects for thin pack resolution.
    /// </summary>
    private static async Task ExportObjectGraphAsync(
        string repositoryId, string sha, string gitDir,
        GitObjectStore objectStore, HashSet<string> visited)
    {
        if (!visited.Add(sha)) return;

        var raw = await objectStore.GetRawObjectAsync(repositoryId, sha);
        if (raw == null) return;

        // Write as loose object (zlib-compressed)
        var compressed = ObjectHasher.ZlibCompress(raw);
        var objDir = Path.Combine(gitDir, "objects", sha[..2]);
        Directory.CreateDirectory(objDir);
        var objPath = Path.Combine(objDir, sha[2..]);
        if (!File.Exists(objPath))
            await File.WriteAllBytesAsync(objPath, compressed);

        // Walk the object graph
        var obj = ObjectHasher.ParseObject(raw);
        switch (obj)
        {
            case GitCommit commit:
                await ExportObjectGraphAsync(repositoryId, commit.TreeSha, gitDir, objectStore, visited);
                foreach (var parent in commit.ParentShas)
                    await ExportObjectGraphAsync(repositoryId, parent, gitDir, objectStore, visited);
                break;
            case GitTree tree:
                foreach (var entry in tree.Entries)
                    await ExportObjectGraphAsync(repositoryId, entry.Sha, gitDir, objectStore, visited);
                break;
            case GitTag tag:
                await ExportObjectGraphAsync(repositoryId, tag.ObjectSha, gitDir, objectStore, visited);
                break;
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

        // Second pass: resolve deltas (may need multiple iterations for chained deltas)
        var unresolvedCount = entries.Count(e => e.DeltaData != null);
        var maxIterations = 10;
        while (unresolvedCount > 0 && maxIterations-- > 0)
        {
            var resolvedThisPass = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.DeltaData == null || entry.Sha != null) continue;

                byte[]? baseData = null;
                string baseType = "blob";

                // OFS_DELTA: base referenced by pack offset
                if (entry.BaseOffset > 0)
                {
                    var baseEntry = entries.FirstOrDefault(e => e.PackOffset == entry.BaseOffset);
                    if (baseEntry?.Sha != null && resolved.TryGetValue(baseEntry.Sha, out var ofsData))
                    {
                        baseData = ofsData;
                        baseType = baseEntry.ObjectType switch
                        {
                            GitObjectType.Commit => "commit",
                            GitObjectType.Tree => "tree",
                            GitObjectType.Blob => "blob",
                            GitObjectType.Tag => "tag",
                            _ => "blob"
                        };
                    }
                }
                // REF_DELTA: base referenced by SHA
                else if (entry.BaseSha != null)
                {
                    if (resolved.TryGetValue(entry.BaseSha, out var refData))
                    {
                        baseData = refData;
                    }
                    else
                    {
                        var baseObj = await objectStore.GetObjectAsync(repository.id, entry.BaseSha);
                        if (baseObj != null)
                        {
                            baseData = baseObj.SerializeContent();
                            baseType = baseObj.Type switch
                            {
                                GitObjectType.Commit => "commit",
                                GitObjectType.Tree => "tree",
                                GitObjectType.Blob => "blob",
                                GitObjectType.Tag => "tag",
                                _ => "blob"
                            };
                        }
                    }
                }

                if (baseData == null) continue;

                var result = PackFile.ApplyDelta(baseData, entry.DeltaData);
                var header = Encoding.ASCII.GetBytes($"{baseType} {result.Length}\0");
                var raw = new byte[header.Length + result.Length];
                Buffer.BlockCopy(header, 0, raw, 0, header.Length);
                Buffer.BlockCopy(result, 0, raw, header.Length, result.Length);

                entry.Sha = ObjectHasher.HashRaw(raw);
                entry.ObjectType = baseType switch
                {
                    "commit" => GitObjectType.Commit,
                    "tree" => GitObjectType.Tree,
                    "tag" => GitObjectType.Tag,
                    _ => GitObjectType.Blob
                };
                resolved[entry.Sha] = result;
                await objectStore.StoreRawObjectAsync(repository, raw, baseType);
                resolvedThisPass++;
            }

            unresolvedCount -= resolvedThisPass;
            if (resolvedThisPass == 0) break;
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

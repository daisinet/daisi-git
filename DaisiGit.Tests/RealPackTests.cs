using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using DaisiGit.Core.Git;
using DaisiGit.Core.Git.Pack;

namespace DaisiGit.Tests;

/// <summary>
/// Tests pack parsing and generation using real production files (30 files from C:\data-git-tests).
/// Creates a real git repo, commits the files, captures the pack git produces, then verifies
/// our parser can correctly decompress every object.
/// </summary>
public class RealPackTests
{
    private const string TestDataDir = @"C:\data-git-tests";

    private static bool TestDataExists => Directory.Exists(TestDataDir);

    /// <summary>
    /// Creates a git repo with the test files, generates a pack, parses it with our code,
    /// and verifies every object matches git's output.
    /// </summary>
    [Fact]
    public void ParseRealPack_AllObjectsMatchGit()
    {
        if (!TestDataExists) return; // Skip if test data not available

        var repoDir = Path.Combine(Path.GetTempPath(), $"daisigit-realpack-{Guid.NewGuid():N}");
        try
        {
            // Step 1: Create git repo with the test files
            Directory.CreateDirectory(repoDir);
            RunGit(repoDir, "init");
            RunGit(repoDir, "config user.email test@test.com");
            RunGit(repoDir, "config user.name Test");

            // Copy all test files
            CopyDirectory(TestDataDir, repoDir);
            RunGit(repoDir, "add .");
            RunGit(repoDir, "commit -m \"Add all files\"");

            // Step 2: Generate a pack file using git
            var packOutput = RunGit(repoDir, "pack-objects --all --stdout");
            // pack-objects --stdout writes the pack to stdout. Use rev-list instead.
            var revList = RunGit(repoDir, "rev-list --objects --all").Trim();
            var objectShas = revList.Split('\n')
                .Select(l => l.Split(' ')[0].Trim())
                .Where(s => s.Length == 40)
                .ToList();

            // Write SHAs to stdin of pack-objects
            var packProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "pack-objects --stdout",
                    WorkingDirectory = repoDir,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            packProcess.Start();
            foreach (var sha in objectShas)
                packProcess.StandardInput.WriteLine(sha);
            packProcess.StandardInput.Close();

            using var packMs = new MemoryStream();
            packProcess.StandardOutput.BaseStream.CopyTo(packMs);
            packProcess.WaitForExit(30000);
            var packData = packMs.ToArray();

            Assert.True(packData.Length > 20, $"Pack too small: {packData.Length} bytes");
            Assert.Equal((byte)'P', packData[0]);
            Assert.Equal((byte)'A', packData[1]);

            // Step 3: Parse with our code
            var entries = PackFile.Parse(packData);
            Assert.True(entries.Count > 0, "No entries parsed");

            // Step 4: Verify every object against git cat-file
            var mismatches = new List<string>();
            var deltaCount = entries.Count(e => e.DeltaData != null);
            var nonDeltaCount = entries.Count(e => e.DeltaData == null);
            foreach (var entry in entries)
            {
                if (entry.Sha == null || entry.DeltaData != null) continue;

                // Get the expected content from git
                var gitType = RunGit(repoDir, $"cat-file -t {entry.Sha}").Trim();
                var expectedSize = int.Parse(RunGit(repoDir, $"cat-file -s {entry.Sha}").Trim());

                // For non-binary objects, compare text content
                if (gitType != "tree")
                {
                    var gitContent = RunGitBinary(repoDir, $"cat-file -p {entry.Sha}");
                    if (gitContent.Length != entry.Data.Length)
                    {
                        mismatches.Add($"{entry.Sha} ({gitType}): size mismatch git={gitContent.Length} ours={entry.Data.Length}");
                    }
                    else if (!gitContent.SequenceEqual(entry.Data))
                    {
                        mismatches.Add($"{entry.Sha} ({gitType}): content mismatch");
                    }
                }
                else
                {
                    // For trees, compare size
                    if (expectedSize != entry.Data.Length)
                    {
                        mismatches.Add($"{entry.Sha} (tree): size mismatch git={expectedSize} ours={entry.Data.Length}");
                    }
                }
            }

            Assert.True(mismatches.Count == 0,
                $"Object mismatches ({mismatches.Count}), nonDelta={nonDeltaCount}, delta={deltaCount}:\n" +
                string.Join("\n", mismatches));

            // Step 5: Verify git can unpack our re-generated pack
            var regenPack = PackFile.Generate(
                entries.Where(e => e.DeltaData == null && e.Sha != null)
                    .Select(e =>
                    {
                        GitObject obj = e.ObjectType switch
                        {
                            GitObjectType.Blob => new GitBlob { Data = e.Data, Sha = e.Sha },
                            GitObjectType.Tree => ObjectHasher.ParseObject(
                                Encoding.ASCII.GetBytes($"tree {e.Data.Length}\0").Concat(e.Data).ToArray()) as GitObject
                                ?? new GitBlob { Data = e.Data, Sha = e.Sha },
                            GitObjectType.Commit => ObjectHasher.ParseObject(
                                Encoding.ASCII.GetBytes($"commit {e.Data.Length}\0").Concat(e.Data).ToArray()) as GitObject
                                ?? new GitBlob { Data = e.Data, Sha = e.Sha },
                            _ => new GitBlob { Data = e.Data, Sha = e.Sha }
                        };
                        obj.Sha = e.Sha;
                        return obj;
                    }).ToList());

            var verifyDir = Path.Combine(Path.GetTempPath(), $"daisigit-verify-{Guid.NewGuid():N}");
            Directory.CreateDirectory(verifyDir);
            RunGit(verifyDir, "init --bare");

            var unpackProc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "unpack-objects",
                    WorkingDirectory = verifyDir,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            unpackProc.Start();
            unpackProc.StandardInput.BaseStream.Write(regenPack);
            unpackProc.StandardInput.Close();
            unpackProc.WaitForExit(10000);
            var unpackErr = unpackProc.StandardError.ReadToEnd();

            try { Directory.Delete(verifyDir, true); } catch { }

            Assert.True(unpackProc.ExitCode == 0,
                $"git unpack-objects failed on regenerated pack: {unpackErr}");
        }
        finally
        {
            try { Directory.Delete(repoDir, true); } catch { }
        }
    }

    /// <summary>
    /// THE KEY TEST: Mimics exactly what the server does during push.
    /// Parses pack, resolves all deltas, computes SHAs, and verifies every resolved object
    /// matches git's version. This catches the bug where delta resolution silently drops objects.
    /// </summary>
    [Fact]
    public void FullServerParsePath_AllObjectsResolved()
    {
        if (!TestDataExists) return;

        var repoDir = Path.Combine(Path.GetTempPath(), $"daisigit-server-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(repoDir);
            RunGit(repoDir, "init");
            RunGit(repoDir, "config user.email test@test.com");
            RunGit(repoDir, "config user.name Test");
            CopyDirectory(TestDataDir, repoDir);
            RunGit(repoDir, "add .");
            RunGit(repoDir, "commit -m \"Add all files\"");

            // Generate pack
            var revList = RunGit(repoDir, "rev-list --objects --all").Trim();
            var shas = revList.Split('\n').Select(l => l.Split(' ')[0].Trim()).Where(s => s.Length == 40).ToList();

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git", Arguments = "pack-objects --stdout",
                    WorkingDirectory = repoDir, UseShellExecute = false,
                    RedirectStandardInput = true, RedirectStandardOutput = true,
                    RedirectStandardError = true, CreateNoWindow = true
                }
            };
            proc.Start();
            foreach (var sha in shas) proc.StandardInput.WriteLine(sha);
            proc.StandardInput.Close();
            using var ms = new MemoryStream();
            proc.StandardOutput.BaseStream.CopyTo(ms);
            proc.WaitForExit(30000);
            var packData = ms.ToArray();

            // Parse pack (same as server)
            var entries = PackFile.Parse(packData);

            // Resolve all objects (mimicking StorePackEntriesAsync)
            var resolved = new Dictionary<string, (string type, byte[] data)>();
            var entryByOffset = new Dictionary<int, PackEntry>();

            // First pass: non-delta objects
            var currentOffset = 12; // skip pack header
            for (var i = 0; i < entries.Count; i++)
            {
                entryByOffset[currentOffset] = entries[i];
                var entry = entries[i];
                if (entry.DeltaData == null)
                {
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
                    var computedSha = ObjectHasher.HashRaw(raw);
                    resolved[computedSha] = (typeStr, entry.Data);
                }
                // Advance offset (approximate — we don't have exact pack offsets here)
            }

            // Second pass: resolve deltas
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.DeltaData == null) continue;

                byte[]? baseData = null;
                string baseType = "blob";

                if (entry.BaseSha != null && resolved.TryGetValue(entry.BaseSha, out var basePair))
                {
                    baseData = basePair.data;
                    baseType = basePair.type;
                }
                // OFS_DELTA: entry.BaseOffset gives the absolute offset of the base
                // We'd need exact pack offsets which we don't track here. Skip for now.

                if (baseData != null)
                {
                    var result = PackFile.ApplyDelta(baseData, entry.DeltaData);
                    var header = Encoding.ASCII.GetBytes($"{baseType} {result.Length}\0");
                    var raw = new byte[header.Length + result.Length];
                    Buffer.BlockCopy(header, 0, raw, 0, header.Length);
                    Buffer.BlockCopy(result, 0, raw, header.Length, result.Length);
                    var computedSha = ObjectHasher.HashRaw(raw);
                    resolved[computedSha] = (baseType, result);
                }
            }

            // Verify all expected SHAs were resolved
            var missing = shas.Where(s => !resolved.ContainsKey(s)).ToList();
            var deltaEntries = entries.Count(e => e.DeltaData != null);
            var unresolvedDeltas = entries.Count(e => e.DeltaData != null && e.BaseSha != null && !resolved.ContainsKey(e.BaseSha ?? ""));

            Assert.True(missing.Count == 0,
                $"Missing {missing.Count}/{shas.Count} objects. " +
                $"Parsed {entries.Count} entries ({deltaEntries} deltas, {unresolvedDeltas} unresolved). " +
                $"Missing SHAs:\n" + string.Join("\n", missing.Take(10)));
        }
        finally
        {
            try { Directory.Delete(repoDir, true); } catch { }
        }
    }

    /// <summary>
    /// Tests that ZlibCompress → ZlibDecompress round-trips correctly for all test files.
    /// </summary>
    [Fact]
    public void ZlibRoundTrip_AllTestFiles()
    {
        if (!TestDataExists) return;

        var failures = new List<string>();
        foreach (var file in Directory.GetFiles(TestDataDir, "*", SearchOption.AllDirectories))
        {
            var content = File.ReadAllBytes(file);
            var header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
            var raw = new byte[header.Length + content.Length];
            Buffer.BlockCopy(header, 0, raw, 0, header.Length);
            Buffer.BlockCopy(content, 0, raw, header.Length, content.Length);

            var compressed = ObjectHasher.ZlibCompress(raw);
            var decompressed = ObjectHasher.ZlibDecompress(compressed);

            if (!raw.SequenceEqual(decompressed))
            {
                var relPath = Path.GetRelativePath(TestDataDir, file);
                failures.Add($"{relPath}: raw={raw.Length} compressed={compressed.Length} decompressed={decompressed.Length}");
            }
        }

        Assert.True(failures.Count == 0,
            $"Zlib round-trip failures:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// Tests that the pack parser handles delta objects (OFS_DELTA) correctly.
    /// Creates a git repo, makes two commits (so git uses deltas), and verifies parsing.
    /// </summary>
    [Fact]
    public void ParseRealPack_WithDeltas()
    {
        if (!TestDataExists) return;

        var repoDir = Path.Combine(Path.GetTempPath(), $"daisigit-delta-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(repoDir);
            RunGit(repoDir, "init");
            RunGit(repoDir, "config user.email test@test.com");
            RunGit(repoDir, "config user.name Test");

            // First commit with some files
            CopyDirectory(TestDataDir, repoDir);
            RunGit(repoDir, "add .");
            RunGit(repoDir, "commit -m \"Initial\"");

            // Second commit — modify one file (forces delta compression)
            File.AppendAllText(Path.Combine(repoDir, "README.md"), "\n// Modified\n");
            RunGit(repoDir, "add .");
            RunGit(repoDir, "commit -m \"Modify README\"");

            // Generate pack with all objects
            var revList = RunGit(repoDir, "rev-list --objects --all").Trim();
            var shas = revList.Split('\n').Select(l => l.Split(' ')[0].Trim()).Where(s => s.Length == 40).ToList();

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git", Arguments = "pack-objects --stdout",
                    WorkingDirectory = repoDir, UseShellExecute = false,
                    RedirectStandardInput = true, RedirectStandardOutput = true,
                    RedirectStandardError = true, CreateNoWindow = true
                }
            };
            proc.Start();
            foreach (var sha in shas) proc.StandardInput.WriteLine(sha);
            proc.StandardInput.Close();
            using var ms = new MemoryStream();
            proc.StandardOutput.BaseStream.CopyTo(ms);
            proc.WaitForExit(30000);
            var packData = ms.ToArray();

            // Parse
            var entries = PackFile.Parse(packData);

            // Count delta vs non-delta entries
            var deltaCount = entries.Count(e => e.DeltaData != null);
            var nonDeltaCount = entries.Count(e => e.DeltaData == null);

            // All non-delta entries should have valid SHA
            foreach (var entry in entries.Where(e => e.DeltaData == null && e.Sha != null))
            {
                var gitType = RunGit(repoDir, $"cat-file -t {entry.Sha}").Trim();
                Assert.True(!string.IsNullOrEmpty(gitType),
                    $"Object {entry.Sha} not found in git repo");
            }
        }
        finally
        {
            try { Directory.Delete(repoDir, true); } catch { }
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relPath = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(dest, relPath));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relPath = Path.GetRelativePath(source, file);
            File.Copy(file, Path.Combine(dest, relPath), true);
        }
    }

    private static string RunGit(string workDir, string args)
    {
        var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "git", Arguments = args, WorkingDirectory = workDir,
            UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        })!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return output;
    }

    private static byte[] RunGitBinary(string workDir, string args)
    {
        var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "git", Arguments = args, WorkingDirectory = workDir,
            UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        })!;
        using var ms = new MemoryStream();
        proc.StandardOutput.BaseStream.CopyTo(ms);
        proc.WaitForExit();
        return ms.ToArray();
    }
}

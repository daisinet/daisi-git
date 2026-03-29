using System.IO.Compression;
using System.Reflection;
using System.Text;
using DaisiGit.Core.Git;
using DaisiGit.Core.Git.Pack;

namespace DaisiGit.Tests;

public class PackFileTests
{
    [Fact]
    public void Generate_ProducesValidPackHeader()
    {
        var blob = new GitBlob { Data = "test"u8.ToArray() };
        blob.Sha = ObjectHasher.HashObject(blob);

        var packData = PackFile.Generate([blob]);

        // Verify PACK signature
        Assert.Equal((byte)'P', packData[0]);
        Assert.Equal((byte)'A', packData[1]);
        Assert.Equal((byte)'C', packData[2]);
        Assert.Equal((byte)'K', packData[3]);

        // Verify version 2
        Assert.Equal(0, packData[4]);
        Assert.Equal(0, packData[5]);
        Assert.Equal(0, packData[6]);
        Assert.Equal(2, packData[7]);

        // Verify object count = 1
        Assert.Equal(0, packData[8]);
        Assert.Equal(0, packData[9]);
        Assert.Equal(0, packData[10]);
        Assert.Equal(1, packData[11]);
    }

    [Fact]
    public void Generate_Parse_RoundTrips()
    {
        var blob1 = new GitBlob { Data = "Hello World\n"u8.ToArray() };
        blob1.Sha = ObjectHasher.HashObject(blob1);

        var blob2 = new GitBlob { Data = "Another file\n"u8.ToArray() };
        blob2.Sha = ObjectHasher.HashObject(blob2);

        var packData = PackFile.Generate([blob1, blob2]);
        var entries = PackFile.Parse(packData);

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(GitObjectType.Blob, e.ObjectType));
    }

    [Fact]
    public void ApplyDelta_CopyInstruction_Works()
    {
        var baseData = "Hello, World!"u8.ToArray();

        // Simple delta: copy all from base
        var delta = new byte[]
        {
            13, // base size
            13, // result size
            0x80 | 0x10 | 0x01, // copy: offset present (bit 0), size present (bit 4)
            0, // offset = 0
            13 // size = 13
        };

        var result = PackFile.ApplyDelta(baseData, delta);
        Assert.Equal(baseData, result);
    }

    [Fact]
    public void ApplyDelta_InsertInstruction_Works()
    {
        var baseData = "Hello"u8.ToArray();

        // Delta: insert new data ", World!"
        var insertData = ", World!"u8.ToArray();
        var delta = new List<byte>
        {
            5, // base size = 5
            13 // result size = 13
        };
        // Copy "Hello" from base
        delta.Add(0x80 | 0x10 | 0x01); // copy
        delta.Add(0); // offset = 0
        delta.Add(5); // size = 5
        // Insert ", World!"
        delta.Add((byte)insertData.Length); // insert cmd
        delta.AddRange(insertData);

        var result = PackFile.ApplyDelta(baseData, delta.ToArray());
        Assert.Equal("Hello, World!", System.Text.Encoding.UTF8.GetString(result));
    }

    [Fact]
    public void Generate_CommitTreeBlob_PackCanBeUnpackedByGit()
    {
        // Build a realistic pack with commit + tree + blob (like initial commit)
        var blob = new GitBlob { Data = "# Test Repo\n"u8.ToArray() };
        blob.Sha = ObjectHasher.HashObject(blob);

        var tree = new GitTree
        {
            Entries = [new GitTreeEntry { Mode = "100644", Name = "README.md", Sha = blob.Sha }]
        };
        tree.Sha = ObjectHasher.HashObject(tree);

        var sig = new GitSignature { Name = "Test", Email = "test@test.com", Timestamp = DateTimeOffset.UtcNow };
        var commit = new GitCommit
        {
            TreeSha = tree.Sha,
            ParentShas = [],
            Author = sig,
            Committer = sig,
            Message = "Initial commit"
        };
        commit.Sha = ObjectHasher.HashObject(commit);

        var packData = PackFile.Generate([commit, tree, blob]);
        var packPath = Path.Combine(Path.GetTempPath(), $"daisigit-test-{Guid.NewGuid():N}.pack");
        var repoPath = Path.Combine(Path.GetTempPath(), $"daisigit-test-{Guid.NewGuid():N}");

        try
        {
            File.WriteAllBytes(packPath, packData);
            Directory.CreateDirectory(repoPath);
            RunGit(repoPath, "init --bare");

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "unpack-objects",
                    WorkingDirectory = repoPath,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.StandardInput.BaseStream.Write(packData);
            process.StandardInput.Close();
            process.WaitForExit(10000);

            var stderr = process.StandardError.ReadToEnd();
            Assert.True(process.ExitCode == 0,
                $"git unpack-objects failed (exit {process.ExitCode}): {stderr}");

            // Verify git can read the objects
            var catResult = RunGit(repoPath, $"cat-file -t {commit.Sha}");
            Assert.Contains("commit", catResult);
        }
        finally
        {
            try { File.Delete(packPath); } catch { }
            try { Directory.Delete(repoPath, true); } catch { }
        }
    }

    [Fact]
    public void Generate_CommitWithEmptyTree_PackCanBeUnpackedByGit()
    {
        // Reproduce exact scenario from test-company-repo: commit + empty tree
        var tree = new GitTree { Entries = [] };
        tree.Sha = ObjectHasher.HashObject(tree);
        Assert.Equal("4b825dc642cb6eb9a060e54bf8d69288fbee4904", tree.Sha);
        Assert.Empty(tree.SerializeContent()); // 0 bytes

        var sig = new GitSignature { Name = "daisinet", Email = "daisinet@daisigit.local",
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(1774473350) };
        var commit = new GitCommit
        {
            TreeSha = tree.Sha,
            ParentShas = [],
            Author = sig,
            Committer = sig,
            Message = "Initial commit"
        };
        commit.Sha = ObjectHasher.HashObject(commit);

        // Test with just the empty tree first
        var emptyTreePack = PackFile.Generate([tree]);
        var emptyRepoPath = Path.Combine(Path.GetTempPath(), $"daisigit-et-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyRepoPath);
        RunGit(emptyRepoPath, "init --bare");
        var etProc = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git", Arguments = "unpack-objects", WorkingDirectory = emptyRepoPath,
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true
            }
        };
        etProc.Start();
        etProc.StandardInput.BaseStream.Write(emptyTreePack);
        etProc.StandardInput.Close();
        etProc.WaitForExit(10000);
        var etStderr = etProc.StandardError.ReadToEnd();
        try { Directory.Delete(emptyRepoPath, true); } catch { }
        Assert.True(etProc.ExitCode == 0,
            $"Empty tree pack failed (exit {etProc.ExitCode}): {etStderr}\nPack hex: {Convert.ToHexString(emptyTreePack)}");

        var packData = PackFile.Generate([commit, tree]);

        var repoPath = Path.Combine(Path.GetTempPath(), $"daisigit-empty-tree-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(repoPath);
            RunGit(repoPath, "init --bare");

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "unpack-objects",
                    WorkingDirectory = repoPath,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.StandardInput.BaseStream.Write(packData);
            process.StandardInput.Close();
            process.WaitForExit(10000);

            var stderr = process.StandardError.ReadToEnd();
            Assert.True(process.ExitCode == 0,
                $"git unpack-objects failed (exit {process.ExitCode}): {stderr}");
        }
        finally
        {
            try { Directory.Delete(repoPath, true); } catch { }
        }
    }

    [Fact]
    public void Parse_MultiObjectPack_RoundTripsCorrectly()
    {
        // Generate a pack with many objects of varying sizes to stress-test DeflateFromPack
        var objects = new List<GitObject>();
        for (var i = 0; i < 20; i++)
        {
            var blob = new GitBlob { Data = Encoding.UTF8.GetBytes($"File content #{i}\n" + new string('x', i * 50)) };
            blob.Sha = ObjectHasher.HashObject(blob);
            objects.Add(blob);
        }

        var packData = PackFile.Generate(objects);
        var entries = PackFile.Parse(packData);

        Assert.Equal(20, entries.Count);

        // Verify each entry has the correct content
        for (var i = 0; i < 20; i++)
        {
            var expected = Encoding.UTF8.GetBytes($"File content #{i}\n" + new string('x', i * 50));
            Assert.Equal(expected, entries[i].Data);
        }
    }

    [Fact]
    public void Generate_ManyObjects_PackCanBeUnpackedByGit()
    {
        // Build a realistic repo with multiple files
        var blobs = new List<GitObject>();
        var treeEntries = new List<GitTreeEntry>();
        for (var i = 0; i < 10; i++)
        {
            var blob = new GitBlob { Data = Encoding.UTF8.GetBytes($"// File {i}\nclass C{i} {{ }}") };
            blob.Sha = ObjectHasher.HashObject(blob);
            blobs.Add(blob);
            treeEntries.Add(new GitTreeEntry { Mode = "100644", Name = $"file{i}.cs", Sha = blob.Sha });
        }

        var tree = new GitTree { Entries = treeEntries };
        tree.Sha = ObjectHasher.HashObject(tree);

        var sig = new GitSignature { Name = "Test", Email = "test@test.com", Timestamp = DateTimeOffset.UtcNow };
        var commit = new GitCommit
        {
            TreeSha = tree.Sha, ParentShas = [], Author = sig, Committer = sig, Message = "Add files"
        };
        commit.Sha = ObjectHasher.HashObject(commit);

        var allObjects = new List<GitObject> { commit, tree };
        allObjects.AddRange(blobs);

        var packData = PackFile.Generate(allObjects);
        var repoPath = Path.Combine(Path.GetTempPath(), $"daisigit-multi-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(repoPath);
            RunGit(repoPath, "init --bare");

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git", Arguments = "unpack-objects", WorkingDirectory = repoPath,
                    UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                    RedirectStandardError = true, CreateNoWindow = true
                }
            };
            process.Start();
            process.StandardInput.BaseStream.Write(packData);
            process.StandardInput.Close();
            process.WaitForExit(10000);
            var stderr = process.StandardError.ReadToEnd();
            Assert.True(process.ExitCode == 0, $"git unpack-objects failed: {stderr}");

            // Verify all objects readable
            var catResult = RunGit(repoPath, $"cat-file -t {commit.Sha}");
            Assert.Contains("commit", catResult);
            for (var i = 0; i < 10; i++)
            {
                var content = RunGit(repoPath, $"cat-file -p {blobs[i].Sha}");
                Assert.Contains($"File {i}", content);
            }
        }
        finally
        {
            try { Directory.Delete(repoPath, true); } catch { }
        }
    }

    [Fact]
    public void Generate_SingleBlob_PackCanBeUnpackedByGit()
    {
        var blob = new GitBlob { Data = "Hello from DaisiGit\n"u8.ToArray() };
        blob.Sha = ObjectHasher.HashObject(blob);

        var packData = PackFile.Generate([blob]);
        var packPath = Path.Combine(Path.GetTempPath(), $"daisigit-test-{Guid.NewGuid():N}.pack");
        var repoPath = Path.Combine(Path.GetTempPath(), $"daisigit-test-{Guid.NewGuid():N}");

        try
        {
            File.WriteAllBytes(packPath, packData);

            // Create a bare git repo and try to unpack
            Directory.CreateDirectory(repoPath);
            var initResult = RunGit(repoPath, "init --bare");
            Assert.Contains("Initialized", initResult);

            // Feed the pack to git unpack-objects
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "unpack-objects",
                    WorkingDirectory = repoPath,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.StandardInput.BaseStream.Write(packData);
            process.StandardInput.Close();
            process.WaitForExit(10000);

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            Assert.True(process.ExitCode == 0,
                $"git unpack-objects failed (exit {process.ExitCode}): {stderr}");
        }
        finally
        {
            try { File.Delete(packPath); } catch { }
            try { Directory.Delete(repoPath, true); } catch { }
        }
    }

    private static string RunGit(string workDir, string args)
    {
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        })!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return output;
    }
}

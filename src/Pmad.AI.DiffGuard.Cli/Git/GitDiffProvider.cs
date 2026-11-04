using System.Diagnostics;
using System.Text;
using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Git;

public sealed class GitDiffProvider : IGitDiffProvider
{
    public static readonly GitDiffProvider Instance = new();

    private readonly string _gitPath;

    public GitDiffProvider(string gitPath = "git")
    {
        _gitPath = gitPath;
    }

    public async Task<GitDiffMetadata> GetDiffAsync(
        string repoRoot,
        GitComparisonTarget target,
        bool skipBaseline,
        IEnumerable<string> excludedGlobs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);
        ArgumentNullException.ThrowIfNull(target);

        string repoFull = Path.GetFullPath(repoRoot);

        if (target is GitComparisonTarget.PatchFile patch)
        {
            string patchFullPath = Path.IsPathRooted(patch.FilePath) ? patch.FilePath : Path.Combine(repoFull, patch.FilePath);
            if (!File.Exists(patchFullPath))
            {
                throw new FileNotFoundException($"Patch file not found: {patchFullPath}", patchFullPath);
            }

            string diffText = await File.ReadAllTextAsync(patchFullPath, cancellationToken).ConfigureAwait(false);
            RepositoryBaselineStats? patchBaseline = null;
            if (!skipBaseline && IsGitRepo(repoFull))
            {
                patchBaseline = await TryComputeBaselineStatsAsync(repoFull, "HEAD", excludedGlobs, cancellationToken).ConfigureAwait(false);
            }

            return new GitDiffMetadata
            {
                TargetDescription = $"patch file: {patch.FilePath}",
                UnifiedDiffText = diffText,
                BaselineStats = patchBaseline,
                RepositoryRootPath = repoFull
            };
        }

        if (!IsGitRepo(repoFull))
        {
            throw new InvalidOperationException($"Directory is not a Git repository: {repoFull}");
        }

        string? baseRef = null;
        string? headRef = null;
        string? baseSha = null;
        string? headSha = null;
        string unifiedDiffText;
        string targetDesc;

        switch (target)
        {
            case GitComparisonTarget.BranchRange branch:
                ValidateGitRef(branch.BaseRef);
                ValidateGitRef(branch.HeadRef);
                baseRef = branch.BaseRef;
                headRef = branch.HeadRef;
                baseSha = await ResolveCommitShaAsync(repoFull, branch.BaseRef, cancellationToken).ConfigureAwait(false);
                headSha = await ResolveCommitShaAsync(repoFull, branch.HeadRef, cancellationToken).ConfigureAwait(false);

                string diffRange = branch.TwoDot ? $"{branch.BaseRef}..{branch.HeadRef}" : $"{branch.BaseRef}...{branch.HeadRef}";
                targetDesc = diffRange;

                var diffArgs = new List<string>
                {
                    "diff", "--no-color", "--no-ext-diff", "--find-renames", "--full-index", diffRange
                };
                unifiedDiffText = await RunGitAsync(repoFull, diffArgs, cancellationToken).ConfigureAwait(false);
                break;

            case GitComparisonTarget.Staged:
                targetDesc = "--staged";
                headRef = "HEAD";
                headSha = await TryResolveCommitShaAsync(repoFull, "HEAD", cancellationToken).ConfigureAwait(false);
                baseSha = headSha;

                unifiedDiffText = await RunGitAsync(repoFull, ["diff", "--cached", "--no-color", "--no-ext-diff", "--find-renames", "--full-index"], cancellationToken).ConfigureAwait(false);
                break;

            case GitComparisonTarget.WorkingTree:
                targetDesc = "--working-tree";
                headRef = "HEAD";
                headSha = await ResolveCommitShaAsync(repoFull, "HEAD", cancellationToken).ConfigureAwait(false);
                baseSha = headSha;

                unifiedDiffText = await RunGitAsync(repoFull, ["diff", "--no-color", "--no-ext-diff", "--find-renames", "--full-index", "HEAD"], cancellationToken).ConfigureAwait(false);
                break;

            case GitComparisonTarget.Commit commit:
                ValidateGitRef(commit.CommitSha);
                targetDesc = $"commit {commit.CommitSha}";
                headRef = commit.CommitSha;
                headSha = await ResolveCommitShaAsync(repoFull, commit.CommitSha, cancellationToken).ConfigureAwait(false);
                baseSha = await TryResolveCommitShaAsync(repoFull, $"{commit.CommitSha}^", cancellationToken).ConfigureAwait(false);

                unifiedDiffText = await RunGitAsync(repoFull, ["show", "--format=", "--no-color", "--no-ext-diff", "--find-renames", "--full-index", commit.CommitSha], cancellationToken).ConfigureAwait(false);
                break;

            default:
                throw new NotSupportedException($"Unsupported comparison target: {target.GetType().Name}");
        }

        RepositoryBaselineStats? baselineStats = null;
        if (!skipBaseline && !string.IsNullOrEmpty(baseSha))
        {
            baselineStats = await TryComputeBaselineStatsAsync(repoFull, baseSha, excludedGlobs, cancellationToken).ConfigureAwait(false);
        }

        return new GitDiffMetadata
        {
            TargetDescription = targetDesc,
            BaseRef = baseRef,
            HeadRef = headRef,
            BaseCommitSha = baseSha,
            HeadCommitSha = headSha,
            UnifiedDiffText = unifiedDiffText,
            BaselineStats = baselineStats,
            RepositoryRootPath = repoFull
        };
    }

    public Func<FileDiff, CancellationToken, ValueTask<Stream?>> CreateBlobStreamProvider(
        string repoRoot,
        GitComparisonTarget target,
        string? headCommitSha)
    {
        string repoFull = Path.GetFullPath(repoRoot);
        string rootWithSep = Path.TrimEndingDirectorySeparator(repoFull) + Path.DirectorySeparatorChar;

        return (file, ct) =>
        {
            if (IsPathTraversal(file.FilePath))
            {
                return ValueTask.FromResult<Stream?>(null);
            }

            if (target is GitComparisonTarget.WorkingTree)
            {
                string targetFull = Path.GetFullPath(Path.Combine(repoFull, file.FilePath));
                if (targetFull.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(targetFull) &&
                    !HasReparsePointInHierarchy(repoFull, targetFull))
                {
                    Stream fs = new FileStream(targetFull, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return ValueTask.FromResult<Stream?>(fs);
                }
                return ValueTask.FromResult<Stream?>(null);
            }

            if (!string.IsNullOrEmpty(file.NewBlobSha))
            {
                var s = OpenCatFileStream(repoFull, file.NewBlobSha);
                if (s != null)
                {
                    return ValueTask.FromResult<Stream?>(s);
                }
            }

            if (target is GitComparisonTarget.Staged)
            {
                var s = OpenCatFileStream(repoFull, $":{file.FilePath}");
                return ValueTask.FromResult(s);
            }

            if (!string.IsNullOrEmpty(headCommitSha))
            {
                var s = OpenCatFileStream(repoFull, $"{headCommitSha}:{file.FilePath}");
                if (s != null)
                {
                    return ValueTask.FromResult<Stream?>(s);
                }
            }

            if (target is GitComparisonTarget.PatchFile)
            {
                string targetFull = Path.GetFullPath(Path.Combine(repoFull, file.FilePath));
                if (targetFull.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(targetFull) &&
                    !HasReparsePointInHierarchy(repoFull, targetFull))
                {
                    Stream fs = new FileStream(targetFull, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return ValueTask.FromResult<Stream?>(fs);
                }
            }

            return ValueTask.FromResult<Stream?>(null);
        };
    }

    private Stream? OpenCatFileStream(string repoPath, string blobOrRevPath)
    {
        if (string.IsNullOrWhiteSpace(blobOrRevPath) || blobOrRevPath.StartsWith('-'))
        {
            return null;
        }

        Process? proc = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _gitPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(repoPath);
            psi.ArgumentList.Add("cat-file");
            psi.ArgumentList.Add("blob");
            psi.ArgumentList.Add(blobOrRevPath);

            proc = Process.Start(psi);
            if (proc == null) return null;

            _ = proc.StandardError.ReadToEndAsync();

            var baseStream = proc.StandardOutput.BaseStream;
            byte[] prefixBuffer = new byte[512];
            int totalRead = baseStream.ReadAtLeast(prefixBuffer, 512, throwOnEndOfStream: false);

            if (totalRead < 512)
            {
                proc.WaitForExit(2000);
                if (proc.HasExited && proc.ExitCode != 0)
                {
                    proc.Dispose();
                    return null;
                }

                proc.Dispose();
                return new MemoryStream(prefixBuffer, 0, totalRead, writable: false);
            }

            return new GitProcessStream(prefixBuffer, totalRead, proc, baseStream);
        }
        catch
        {
            if (proc != null)
            {
                try
                {
                    if (!proc.HasExited) proc.Kill();
                }
                catch
                {
                }
                proc.Dispose();
            }
            return null;
        }
    }

    private async Task<string> ResolveCommitShaAsync(string repoRoot, string gitRef, CancellationToken cancellationToken)
    {
        try
        {
            string output = await RunGitAsync(repoRoot, ["rev-parse", "--verify", $"{gitRef}^{{commit}}"], cancellationToken).ConfigureAwait(false);
            return output.Trim();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Unable to resolve Git commit for ref '{gitRef}': {ex.Message}", ex);
        }
    }

    private async Task<string?> TryResolveCommitShaAsync(string repoRoot, string gitRef, CancellationToken cancellationToken)
    {
        try
        {
            string output = await RunGitAsync(repoRoot, ["rev-parse", "--verify", $"{gitRef}^{{commit}}"], cancellationToken).ConfigureAwait(false);
            return output.Trim();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<RepositoryBaselineStats?> TryComputeBaselineStatsAsync(
        string repoRoot,
        string baseCommitSha,
        IEnumerable<string> excludedGlobs,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Count tracked files using ls-tree -r -z --name-only <baseCommitSha>
            string lsOutput = await RunGitAsync(repoRoot, ["ls-tree", "-r", "-z", "--name-only", baseCommitSha], cancellationToken).ConfigureAwait(false);
            var files = lsOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);

            int totalFiles = 0;
            var excludedList = excludedGlobs.ToList();

            foreach (var f in files)
            {
                if (string.IsNullOrWhiteSpace(f)) continue;
                if (IsExcluded(f, excludedList)) continue;
                totalFiles++;
            }

            // 2. Count total text lines using git grep -I -c "" <baseCommitSha>
            // Note: git grep exits with 1 if no matches are found, so handle non-zero exit code gracefully
            int totalLines = 0;
            try
            {
                string grepOutput = await RunGitAsync(repoRoot, ["grep", "-I", "-c", "", baseCommitSha], cancellationToken).ConfigureAwait(false);
                using var reader = new StringReader(grepOutput);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    // Format: <tree>:<path>:<count>
                    int firstColon = line.IndexOf(':');
                    if (firstColon >= 0)
                    {
                        int lastColon = line.LastIndexOf(':');
                        if (lastColon > firstColon)
                        {
                            string path = line.Substring(firstColon + 1, lastColon - firstColon - 1);
                            string countStr = line.Substring(lastColon + 1);

                            if (int.TryParse(countStr, out int count))
                            {
                                if (!IsExcluded(path, excludedList))
                                {
                                    totalLines += count;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // git grep might fail if empty tree or no text files; treat as 0 lines
            }

            return new RepositoryBaselineStats(totalFiles, totalLines);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsExcluded(string path, IReadOnlyList<string> excludedGlobs)
    {
        if (excludedGlobs.Count == 0) return false;
        string norm = path.Replace('\\', '/');
        foreach (var glob in excludedGlobs)
        {
            if (GlobMatcher.IsMatch(norm, glob))
            {
                return true;
            }
        }
        return false;
    }

    private async Task<string> RunGitAsync(string repoRoot, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _gitPath,
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start git process with arguments: {string.Join(" ", args)}");
        }

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Git command 'git {string.Join(" ", args)}' failed with exit code {process.ExitCode}: {stderr.Trim()}");
            }

            return stdout;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            throw;
        }
    }

    private static void ValidateGitRef(string gitRef)
    {
        if (string.IsNullOrWhiteSpace(gitRef))
        {
            throw new ArgumentException("Git ref cannot be empty.", nameof(gitRef));
        }

        if (gitRef.StartsWith('-'))
        {
            throw new ArgumentException($"Invalid git ref '{gitRef}': git refs cannot start with '-' to prevent option injection.", nameof(gitRef));
        }

        foreach (char c in gitRef)
        {
            if (char.IsControl(c))
            {
                throw new ArgumentException($"Invalid git ref '{gitRef}': contains control characters.", nameof(gitRef));
            }
        }
    }

    private static bool IsGitRepo(string path)
    {
        return Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git"));
    }

    private static bool IsPathTraversal(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || Path.IsPathRooted(filePath))
        {
            return true;
        }

        var segments = filePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        int depth = 0;
        foreach (var seg in segments)
        {
            if (seg == ".") continue;
            if (seg == "..")
            {
                depth--;
                if (depth < 0) return true;
            }
            else
            {
                depth++;
            }
        }
        return false;
    }

    private static bool HasReparsePointInHierarchy(string rootFull, string targetFull)
    {
        try
        {
            if (File.Exists(targetFull))
            {
                var fi = new FileInfo(targetFull);
                if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            }

            string rootTrimmed = Path.TrimEndingDirectorySeparator(rootFull);
            var currentDir = new DirectoryInfo(Path.GetDirectoryName(targetFull) ?? string.Empty);
            while (currentDir != null &&
                   currentDir.FullName.Length > rootTrimmed.Length &&
                   currentDir.FullName.StartsWith(rootTrimmed, StringComparison.OrdinalIgnoreCase))
            {
                if (currentDir.Exists && (currentDir.Attributes & FileAttributes.ReparsePoint) != 0) return true;
                currentDir = currentDir.Parent;
            }
        }
        catch
        {
            return true;
        }
        return false;
    }

    private sealed class GitProcessStream : Stream
    {
        private readonly byte[] _prefixBuffer;
        private readonly int _prefixLength;
        private int _prefixOffset;
        private readonly Process _process;
        private readonly Stream _baseStream;

        public GitProcessStream(byte[] prefixBuffer, int prefixLength, Process process, Stream baseStream)
        {
            _prefixBuffer = prefixBuffer;
            _prefixLength = prefixLength;
            _prefixOffset = 0;
            _process = process;
            _baseStream = baseStream;
        }

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _baseStream.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            if (_prefixOffset < _prefixLength)
            {
                int toCopy = Math.Min(buffer.Length, _prefixLength - _prefixOffset);
                _prefixBuffer.AsSpan(_prefixOffset, toCopy).CopyTo(buffer);
                _prefixOffset += toCopy;
                return toCopy;
            }

            return _baseStream.Read(buffer);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_prefixOffset < _prefixLength)
            {
                int toCopy = Math.Min(buffer.Length, _prefixLength - _prefixOffset);
                _prefixBuffer.AsMemory(_prefixOffset, toCopy).CopyTo(buffer);
                _prefixOffset += toCopy;
                return ValueTask.FromResult(toCopy);
            }

            return _baseStream.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _baseStream.Dispose();
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill();
                    }
                }
                catch
                {
                }
                _process.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _baseStream.DisposeAsync().ConfigureAwait(false);
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                }
            }
            catch
            {
            }
            _process.Dispose();
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}

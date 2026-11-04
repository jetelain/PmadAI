using System.Diagnostics;

namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Scans git diffs for added, modified, or renamed binary and executable files, enforcing path-traversal safety
/// and delegating format verification to <see cref="ISafeBinaryValidator"/> implementations.
/// </summary>
public sealed class BinaryAndImageScanner
{
    private static readonly HashSet<string> KnownBinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".so", ".dylib", ".bin", ".wasm", ".pyc", ".class",
        ".jar", ".war", ".zip", ".tar", ".gz", ".7z", ".nupkg"
    };

    private readonly DiffRiskScannerOptions _options;

    public BinaryAndImageScanner(DiffRiskScannerOptions? options = null)
    {
        _options = options ?? new DiffRiskScannerOptions();
    }

    public async ValueTask<IReadOnlyList<DiffRiskFinding>> ScanAsync(
        IReadOnlyList<FileDiff> files,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EnableBinaryFileRules || files.Count == 0)
        {
            return [];
        }

        var findings = new List<DiffRiskFinding>();

        foreach (var file in files)
        {
            if (file.IsDeletedFile)
            {
                continue;
            }

            string ext = Path.GetExtension(file.FilePath);
            string? oldExt = !string.IsNullOrEmpty(file.OldFilePath) ? Path.GetExtension(file.OldFilePath) : null;
            bool isCandidate = file.IsBinary ||
                               file.NewFileMode == "100755" ||
                               KnownBinaryExtensions.Contains(ext) ||
                               _options.AllowedImageExtensions.Contains(ext) ||
                               (file.IsRenamedFile && (KnownBinaryExtensions.Contains(ext) || (oldExt != null && KnownBinaryExtensions.Contains(oldExt))));

            if (!isCandidate)
            {
                continue;
            }

            // Path-traversal and symlink verification
            if (IsPathTraversal(file.FilePath))
            {
                findings.Add(new DiffRiskFinding(
                    RuleId: "PAY-BIN-002",
                    Title: "Path Traversal Detected",
                    Category: DiffRiskCategory.SuspiciousPayload,
                    Severity: DiffRiskSeverity.Critical,
                    FilePath: file.FilePath,
                    LineNumber: null,
                    Column: null,
                    Description: "Binary file path escapes repository root or targets a symlink/reparse point."));
                continue;
            }

            Func<Stream?> streamOpener = () => null;
            Func<CancellationToken, ValueTask<Stream?>>? asyncStreamOpener = null;

            if (_options.BlobStreamProvider != null)
            {
                asyncStreamOpener = ct => _options.BlobStreamProvider(file, ct);
            }
            else if (_options.FileStreamProvider != null)
            {
                streamOpener = () => _options.FileStreamProvider(file.FilePath);
            }
            else if (!string.IsNullOrEmpty(_options.RepositoryRootPath))
            {
                string rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_options.RepositoryRootPath)) + Path.DirectorySeparatorChar;
                string targetFull = Path.GetFullPath(Path.Combine(rootFull, file.FilePath));

                if (!targetFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new DiffRiskFinding(
                        RuleId: "PAY-BIN-002",
                        Title: "Path Traversal Detected",
                        Category: DiffRiskCategory.SuspiciousPayload,
                        Severity: DiffRiskSeverity.Critical,
                        FilePath: file.FilePath,
                        LineNumber: null,
                        Column: null,
                        Description: "Binary file path escapes repository root or targets a symlink/reparse point."));
                    continue;
                }

                if (HasReparsePointInHierarchy(rootFull, targetFull))
                {
                    findings.Add(new DiffRiskFinding(
                        RuleId: "PAY-BIN-002",
                        Title: "Path Traversal Detected",
                        Category: DiffRiskCategory.SuspiciousPayload,
                        Severity: DiffRiskSeverity.Critical,
                        FilePath: file.FilePath,
                        LineNumber: null,
                        Column: null,
                        Description: "Binary file path escapes repository root or targets a symlink/reparse point."));
                    continue;
                }

                if (IsGitRepository(rootFull) && (!string.IsNullOrEmpty(file.NewBlobSha) || !string.IsNullOrEmpty(_options.TargetGitRef)))
                {
                    string blobTarget = !string.IsNullOrEmpty(file.NewBlobSha)
                        ? file.NewBlobSha
                        : $"{_options.TargetGitRef}:{file.FilePath}";

                    bool allowDiskFallback = _options.TargetGitRef == null && File.Exists(targetFull);
                    streamOpener = () =>
                    {
                        var gitStream = TryOpenGitBlobStream(rootFull, blobTarget);
                        if (gitStream != null)
                        {
                            return gitStream;
                        }

                        return allowDiskFallback
                            ? new FileStream(targetFull, FileMode.Open, FileAccess.Read, FileShare.Read)
                            : null;
                    };
                }
                else if (_options.TargetGitRef == null && File.Exists(targetFull))
                {
                    streamOpener = () => new FileStream(targetFull, FileMode.Open, FileAccess.Read, FileShare.Read);
                }
            }

            // Attempt to pre-buffer header bytes
            ReadOnlyMemory<byte>? headerBytes = null;
            try
            {
                Stream? probeStream = null;
                if (asyncStreamOpener != null)
                {
                    probeStream = await asyncStreamOpener(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    probeStream = streamOpener();
                }

                if (probeStream != null)
                {
                    await using (probeStream.ConfigureAwait(false))
                    {
                        byte[] buffer = new byte[512];
                        int read = await probeStream.ReadAtLeastAsync(buffer, 512, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
                        headerBytes = new ReadOnlyMemory<byte>(buffer, 0, read);
                    }
                }
            }
            catch
            {
                // Stream read errors are handled within validator logic
            }

            var context = new BinaryValidationContext
            {
                FilePath = file.FilePath,
                Extension = ext,
                FileDiff = file,
                StreamOpener = streamOpener,
                AsyncStreamOpener = asyncStreamOpener,
                HeaderBytes = headerBytes
            };

            bool handled = false;
            foreach (var validator in _options.SafeBinaryValidators)
            {
                if (validator.CanValidate(file.FilePath, ext))
                {
                    var result = await validator.ValidateAsync(context, cancellationToken).ConfigureAwait(false);
                    if (result.Handled)
                    {
                        handled = true;
                        if (!result.IsValid)
                        {
                            findings.Add(new DiffRiskFinding(
                                RuleId: result.RuleId ?? "PAY-BIN-002",
                                Title: "Binary Validation Failed",
                                Category: DiffRiskCategory.SuspiciousPayload,
                                Severity: result.Severity,
                                FilePath: file.FilePath,
                                LineNumber: null,
                                Column: null,
                                Description: result.FailureReason ?? "Corrupted, disguised, or dangerous binary file failing safe validation."));
                        }
                        break;
                    }
                }
            }

            if (!handled)
            {
                findings.Add(new DiffRiskFinding(
                    RuleId: "PAY-BIN-001",
                    Title: "Suspicious Non-Image Binary File",
                    Category: DiffRiskCategory.SuspiciousPayload,
                    Severity: DiffRiskSeverity.High,
                    FilePath: file.FilePath,
                    LineNumber: null,
                    Column: null,
                    Description: $"Suspicious non-image binary file added or modified: {file.FilePath}"));
            }
        }

        return findings;
    }

    private static bool IsGitRepository(string rootPath)
    {
        return Directory.Exists(Path.Combine(rootPath, ".git")) || File.Exists(Path.Combine(rootPath, ".git"));
    }

    private static Stream? TryOpenGitBlobStream(string repoPath, string blobOrRevPath)
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
                FileName = "git",
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

            // Drain stderr asynchronously so git never blocks on stderr pipe buffer
            _ = proc.StandardError.ReadToEndAsync();

            var baseStream = proc.StandardOutput.BaseStream;
            byte[] prefixBuffer = new byte[512];
            int totalRead = baseStream.ReadAtLeast(prefixBuffer, 512, throwOnEndOfStream: false);

            if (totalRead < 512)
            {
                // Reached EOF: wait briefly for git process exit code to distinguish empty blob from error
                proc.WaitForExit(2000);
                if (proc.HasExited && proc.ExitCode != 0)
                {
                    proc.Dispose();
                    return null;
                }

                proc.Dispose();
                return new MemoryStream(prefixBuffer, 0, totalRead, writable: false);
            }

            return new ProcessStream(prefixBuffer, totalRead, proc, baseStream);
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

    private static bool IsPathTraversal(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || Path.IsPathRooted(filePath))
        {
            return true;
        }

        // Check normalized segments so relative traversal ("../" or "a/../../b") is always caught
        var segments = filePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        int depth = 0;
        foreach (var seg in segments)
        {
            if (seg == ".")
            {
                continue;
            }
            if (seg == "..")
            {
                depth--;
                if (depth < 0)
                {
                    return true;
                }
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
                if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
            }

            string rootTrimmed = Path.TrimEndingDirectorySeparator(rootFull);
            var currentDir = new DirectoryInfo(Path.GetDirectoryName(targetFull) ?? string.Empty);
            while (currentDir != null &&
                   currentDir.FullName.Length > rootTrimmed.Length &&
                   currentDir.FullName.StartsWith(rootTrimmed, StringComparison.OrdinalIgnoreCase))
            {
                if (currentDir.Exists && (currentDir.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
                currentDir = currentDir.Parent;
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    private sealed class ProcessStream : Stream
    {
        private readonly byte[] _prefixBuffer;
        private readonly int _prefixLength;
        private int _prefixOffset;
        private readonly Process _process;
        private readonly Stream _baseStream;

        public ProcessStream(byte[] prefixBuffer, int prefixLength, Process process, Stream baseStream)
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

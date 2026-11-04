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

            if (_options.FileStreamProvider != null)
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

                if (File.Exists(targetFull))
                {
                    streamOpener = () => new FileStream(targetFull, FileMode.Open, FileAccess.Read, FileShare.Read);
                }
            }

            // Attempt to pre-buffer header bytes
            ReadOnlyMemory<byte>? headerBytes = null;
            try
            {
                using var probeStream = streamOpener();
                if (probeStream != null)
                {
                    byte[] buffer = new byte[512];
                    int read = await probeStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    headerBytes = new ReadOnlyMemory<byte>(buffer, 0, read);
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
}


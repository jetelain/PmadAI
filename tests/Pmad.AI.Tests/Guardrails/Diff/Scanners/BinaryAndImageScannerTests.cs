using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.Tests.Guardrails.Diff.Scanners;

public class BinaryAndImageScannerTests
{
    [Theory]
    [InlineData("lib/native.dll")]
    [InlineData("bin/tools.exe")]
    [InlineData("lib/core.so")]
    [InlineData("assets/blob.bin")]
    public async Task ScanAsync_NonImageBinaries_FlagsPayBin001(string filePath)
    {
        var scanner = new BinaryAndImageScanner();
        var diff = new FileDiff
        {
            FilePath = filePath,
            IsBinary = true
        };

        var findings = await scanner.ScanAsync([diff]);

        Assert.Contains(findings, f => f.RuleId == "PAY-BIN-001" && f.Category == DiffRiskCategory.SuspiciousPayload && f.Severity == DiffRiskSeverity.High);
    }

    [Fact]
    public async Task ScanAsync_RenameOnlyToExe_FlagsPayBin001()
    {
        var scanner = new BinaryAndImageScanner();
        var diff = new FileDiff
        {
            FilePath = "payload.exe",
            OldFilePath = "script.txt",
            IsRenamedFile = true
        };

        var findings = await scanner.ScanAsync([diff]);

        Assert.Contains(findings, f => f.RuleId == "PAY-BIN-001" && f.Category == DiffRiskCategory.SuspiciousPayload);
    }

    [Theory]
    [InlineData("image.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 })]
    [InlineData("photo.jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 })]
    [InlineData("photo.jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xDB })]
    [InlineData("anim.gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00 })]
    [InlineData("icon.ico", new byte[] { 0x00, 0x00, 0x01, 0x00, 0x01, 0x00 })]
    public async Task ScanAsync_ValidImageHeaders_PermitsWithoutFindings(string filePath, byte[] magicHeader)
    {
        var options = new DiffRiskScannerOptions
        {
            FileStreamProvider = _ => new MemoryStream(magicHeader)
        };
        var scanner = new BinaryAndImageScanner(options);
        var diff = new FileDiff
        {
            FilePath = filePath,
            IsBinary = true
        };

        var findings = await scanner.ScanAsync([diff]);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task ScanAsync_ValidWebpHeader_PermitsWithoutFindings()
    {
        byte[] webpHeader =
        [
            0x52, 0x49, 0x46, 0x46, // RIFF
            0x24, 0x00, 0x00, 0x00, // size
            0x57, 0x45, 0x42, 0x50  // WEBP
        ];

        var options = new DiffRiskScannerOptions
        {
            FileStreamProvider = _ => new MemoryStream(webpHeader)
        };
        var scanner = new BinaryAndImageScanner(options);
        var diff = new FileDiff
        {
            FilePath = "test.webp",
            IsBinary = true
        };

        var findings = await scanner.ScanAsync([diff]);

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("fake.png", new byte[] { 0x4D, 0x5A, 0x90, 0x00 })] // MZ DOS/PE
    [InlineData("fake.jpg", new byte[] { 0x7F, 0x45, 0x4C, 0x46 })] // ELF
    [InlineData("fake.gif", new byte[] { 0x50, 0x4B, 0x03, 0x04 })] // PK zip
    public async Task ScanAsync_DisguisedExecutableInImage_FlagsPayBin002Critical(string filePath, byte[] executableHeader)
    {
        var options = new DiffRiskScannerOptions
        {
            FileStreamProvider = _ => new MemoryStream(executableHeader)
        };
        var scanner = new BinaryAndImageScanner(options);
        var diff = new FileDiff
        {
            FilePath = filePath,
            IsBinary = true
        };

        var findings = await scanner.ScanAsync([diff]);

        Assert.Contains(findings, f => f.RuleId == "PAY-BIN-002" && f.Severity == DiffRiskSeverity.Critical);
    }

    [Fact]
    public async Task ScanAsync_CorruptedImageHeader_FlagsPayBin002Critical()
    {
        byte[] corruptedPng = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07];
        var options = new DiffRiskScannerOptions
        {
            FileStreamProvider = _ => new MemoryStream(corruptedPng)
        };
        var scanner = new BinaryAndImageScanner(options);
        var diff = new FileDiff
        {
            FilePath = "corrupted.png",
            IsBinary = true
        };

        var findings = await scanner.ScanAsync([diff]);

        Assert.Contains(findings, f => f.RuleId == "PAY-BIN-002" && f.Severity == DiffRiskSeverity.Critical);
    }

    [Fact]
    public async Task ScanAsync_PathTraversal_RejectsAndFlagsCriticalWithoutOpeningStream()
    {
        string tempRepo = Path.Combine(Path.GetTempPath(), "repo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRepo);
        try
        {
            var options = new DiffRiskScannerOptions
            {
                RepositoryRootPath = tempRepo
            };
            var scanner = new BinaryAndImageScanner(options);
            var diff = new FileDiff
            {
                FilePath = "../../outside.png",
                IsBinary = true
            };

            var findings = await scanner.ScanAsync([diff]);

            Assert.Contains(findings, f => f.RuleId == "PAY-BIN-002" && f.Severity == DiffRiskSeverity.Critical);
        }
        finally
        {
            if (Directory.Exists(tempRepo))
            {
                Directory.Delete(tempRepo, true);
            }
        }
    }

    [Fact]
    public async Task ScanAsync_PathTraversalWithoutRepoRoot_StillRejectsAndFlagsCritical()
    {
        var options = new DiffRiskScannerOptions
        {
            AllowUnverifiedImageExtensionsWhenStreamUnavailable = true
        };
        var scanner = new BinaryAndImageScanner(options);
        var diff = new FileDiff
        {
            FilePath = "../../outside.png",
            IsBinary = true
        };

        var findings = await scanner.ScanAsync([diff]);

        Assert.Contains(findings, f => f.RuleId == "PAY-BIN-002" && f.Severity == DiffRiskSeverity.Critical);
    }

    [Fact]
    public async Task ScanAsync_StreamUnavailable_FailsClosedByDefault()
    {
        var scanner = new BinaryAndImageScanner();
        var diff = new FileDiff
        {
            FilePath = "image.png",
            IsBinary = true
        };

        var findings = await scanner.ScanAsync([diff]);

        Assert.Contains(findings, f => f.RuleId == "PAY-BIN-002" && f.Severity == DiffRiskSeverity.High);
    }

    [Fact]
    public async Task ScanAsync_StreamUnavailable_PermitsWhenExplicitlyAllowed()
    {
        var options = new DiffRiskScannerOptions
        {
            AllowUnverifiedImageExtensionsWhenStreamUnavailable = true
        };
        var scanner = new BinaryAndImageScanner(options);
        var diff = new FileDiff
        {
            FilePath = "image.png",
            IsBinary = true
        };

        var findings = await scanner.ScanAsync([diff]);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task ScanAsync_CustomSafeBinaryValidator_CanPermitOrReject()
    {
        var mockValidator = new MockWasmValidator();
        var options = new DiffRiskScannerOptions
        {
            SafeBinaryValidators = [mockValidator]
        };
        var scanner = new BinaryAndImageScanner(options);

        var validWasm = new FileDiff
        {
            FilePath = "module.wasm",
            IsBinary = true
        };

        var findings = await scanner.ScanAsync([validWasm]);
        Assert.Empty(findings);

        mockValidator.ShouldFail = true;
        var failedFindings = await scanner.ScanAsync([validWasm]);
        Assert.Contains(failedFindings, f => f.RuleId == "WASM-001" && f.Severity == DiffRiskSeverity.High);
    }

    private sealed class MockWasmValidator : ISafeBinaryValidator
    {
        public bool ShouldFail { get; set; }

        public bool CanValidate(string filePath, string extension) =>
            extension.Equals(".wasm", StringComparison.OrdinalIgnoreCase);

        public ValueTask<BinaryValidationResult> ValidateAsync(BinaryValidationContext context, CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                return ValueTask.FromResult(BinaryValidationResult.Invalid("WASM module failed validation", DiffRiskSeverity.High, "WASM-001"));
            }

            return ValueTask.FromResult(BinaryValidationResult.Valid());
        }
    }

    [Fact]
    public async Task ScanAsync_BlobStreamProvider_ResolvesAsyncBlobStream()
    {
        byte[] pngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
        string? observedSha = null;

        var options = new DiffRiskScannerOptions
        {
            BlobStreamProvider = (file, _) =>
            {
                observedSha = file.NewBlobSha;
                return ValueTask.FromResult<Stream?>(new MemoryStream(pngHeader));
            }
        };
        var scanner = new BinaryAndImageScanner(options);
        var diff = new FileDiff
        {
            FilePath = "assets/logo.png",
            NewBlobSha = "abcdef1234567890",
            IsBinary = true
        };

        var findings = await scanner.ScanAsync([diff]);

        Assert.Empty(findings);
        Assert.Equal("abcdef1234567890", observedSha);
    }

    [Fact]
    public async Task ScanAsync_UncheckedOutGitBranch_ReadsTargetBranchBlobInsteadOfBenignWorkingTreeCopy()
    {
        string tempRepo = Path.Combine(Path.GetTempPath(), "git_bin_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRepo);

        static void RunGit(string dir, params string[] args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = dir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.WaitForExit();
            if (p.ExitCode != 0)
            {
                throw new InvalidOperationException($"git {string.Join(" ", args)} failed: {p.StandardError.ReadToEnd()}");
            }
        }

        try
        {
            RunGit(tempRepo, "init", "-b", "main");
            RunGit(tempRepo, "config", "user.name", "Test");
            RunGit(tempRepo, "config", "user.email", "test@example.com");
            RunGit(tempRepo, "config", "commit.gpgSign", "false");

            // Commit a valid PNG on main
            byte[] validPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
            string imgPath = Path.Combine(tempRepo, "logo.png");
            File.WriteAllBytes(imgPath, validPng);
            RunGit(tempRepo, "add", ".");
            RunGit(tempRepo, "commit", "-m", "Add benign logo.png");

            // On feature/evil, replace logo.png with a disguised MZ executable
            RunGit(tempRepo, "checkout", "-b", "feature/evil");
            byte[] disguisedMz = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00];
            File.WriteAllBytes(imgPath, disguisedMz);
            RunGit(tempRepo, "add", ".");
            RunGit(tempRepo, "commit", "-m", "Replace logo.png with MZ executable");

            // Switch back to main so working tree on disk has the benign valid PNG!
            RunGit(tempRepo, "checkout", "main");
            Assert.Equal(validPng, File.ReadAllBytes(imgPath));

            var options = new DiffRiskScannerOptions
            {
                RepositoryRootPath = tempRepo,
                TargetGitRef = "feature/evil"
            };
            var scanner = new BinaryAndImageScanner(options);
            var diff = new FileDiff
            {
                FilePath = "logo.png",
                IsBinary = true
            };

            var findings = await scanner.ScanAsync([diff]);

            var finding = Assert.Single(findings);
            Assert.Equal("PAY-BIN-002", finding.RuleId);
            Assert.Equal(DiffRiskSeverity.Critical, finding.Severity);
            Assert.Contains("MZ", finding.Description);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRepo))
                {
                    foreach (var f in Directory.EnumerateFiles(tempRepo, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(f, FileAttributes.Normal);
                    }
                    Directory.Delete(tempRepo, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task ScanAsync_MissingGitBlobSha_TreatsStreamAsUnavailableRatherThanEmptyFile()
    {
        string tempRepo = Path.Combine(Path.GetTempPath(), "git_missing_sha_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRepo);
        Directory.CreateDirectory(Path.Combine(tempRepo, ".git"));

        try
        {
            var options = new DiffRiskScannerOptions
            {
                RepositoryRootPath = tempRepo,
                AllowUnverifiedImageExtensionsWhenStreamUnavailable = true
            };
            var scanner = new BinaryAndImageScanner(options);
            var diff = new FileDiff
            {
                FilePath = "nonexistent.png",
                NewBlobSha = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef",
                IsBinary = true
            };

            var findings = await scanner.ScanAsync([diff]);

            Assert.Empty(findings);
        }
        finally
        {
            if (Directory.Exists(tempRepo))
            {
                Directory.Delete(tempRepo, recursive: true);
            }
        }
    }
}

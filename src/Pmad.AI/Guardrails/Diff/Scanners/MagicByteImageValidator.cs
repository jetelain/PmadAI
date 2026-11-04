namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Built-in fast, fail-closed magic byte validator for image formats (.png, .jpg, .jpeg, .gif, .webp, .ico).
/// Native AOT compatible with zero external dependencies.
/// </summary>
public sealed class MagicByteImageValidator : ISafeBinaryValidator
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Gif87Magic = [0x47, 0x49, 0x46, 0x38, 0x37, 0x61]; // GIF87a
    private static readonly byte[] Gif89Magic = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61]; // GIF89a
    private static readonly byte[] RiffMagic = [0x52, 0x49, 0x46, 0x46]; // RIFF
    private static readonly byte[] WebpMagic = [0x57, 0x45, 0x42, 0x50]; // WEBP
    private static readonly byte[] IcoMagic = [0x00, 0x00, 0x01, 0x00];

    private static readonly byte[] MzMagic = [0x4D, 0x5A]; // MZ
    private static readonly byte[] ElfMagic = [0x7F, 0x45, 0x4C, 0x46]; // \x7FELF
    private static readonly byte[] PkMagic = [0x50, 0x4B, 0x03, 0x04]; // PK\x03\x04

    private readonly DiffRiskScannerOptions? _options;

    public MagicByteImageValidator(DiffRiskScannerOptions? options = null)
    {
        _options = options;
    }

    public bool CanValidate(string filePath, string extension)
    {
        var allowed = _options?.AllowedImageExtensions ?? DiffRiskDefaults.AllowedImageExtensions;
        return allowed.Contains(extension);
    }

    public async ValueTask<BinaryValidationResult> ValidateAsync(BinaryValidationContext context, CancellationToken cancellationToken = default)
    {
        ReadOnlyMemory<byte>? headerBytes = context.HeaderBytes;
        byte[]? readBuffer = null;
        int readCount = 0;

        if (headerBytes == null)
        {
            try
            {
                var stream = await context.OpenReadAsync(cancellationToken).ConfigureAwait(false);
                if (stream != null)
                {
                    await using (stream.ConfigureAwait(false))
                    {
                        readBuffer = new byte[512];
                        readCount = await stream.ReadAtLeastAsync(readBuffer, 512, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                return BinaryValidationResult.Invalid(
                    $"Unable to read binary image file: {ex.Message}",
                    DiffRiskSeverity.Critical,
                    "PAY-BIN-002");
            }
        }

        if (headerBytes == null && readBuffer == null)
        {
            bool allowUnverified = _options?.AllowUnverifiedImageExtensionsWhenStreamUnavailable ?? false;
            if (allowUnverified)
            {
                return BinaryValidationResult.Valid();
            }

            return BinaryValidationResult.Invalid(
                "Binary image file stream is unavailable; magic-byte signature could not be verified.",
                DiffRiskSeverity.High,
                "PAY-BIN-002");
        }

        ReadOnlySpan<byte> span = headerBytes.HasValue
            ? headerBytes.Value.Span
            : readBuffer.AsSpan(0, readCount);

        if (span.IsEmpty)
        {
            return BinaryValidationResult.Invalid(
                "Binary image file is empty or contains zero readable bytes.",
                DiffRiskSeverity.Critical,
                "PAY-BIN-002");
        }

        // Detect disguised executables or archives
        if (span.Length >= 2 && span.StartsWith(MzMagic))
        {
            return BinaryValidationResult.Invalid(
                "File has an image extension but begins with a DOS/PE executable header (MZ).",
                DiffRiskSeverity.Critical,
                "PAY-BIN-002");
        }

        if (span.Length >= 4 && span.StartsWith(ElfMagic))
        {
            return BinaryValidationResult.Invalid(
                "File has an image extension but begins with an ELF executable header.",
                DiffRiskSeverity.Critical,
                "PAY-BIN-002");
        }

        if (span.Length >= 4 && span.StartsWith(PkMagic))
        {
            return BinaryValidationResult.Invalid(
                "File has an image extension but begins with a ZIP/JAR/executable archive header (PK).",
                DiffRiskSeverity.Critical,
                "PAY-BIN-002");
        }

        // Validate according to claimed extension
        string ext = context.Extension.ToLowerInvariant();
        bool valid = ext switch
        {
            ".png" => span.Length >= PngMagic.Length && span.StartsWith(PngMagic),
            ".jpg" or ".jpeg" => span.Length >= JpegMagic.Length && span.StartsWith(JpegMagic),
            ".gif" => span.Length >= Gif87Magic.Length && (span.StartsWith(Gif87Magic) || span.StartsWith(Gif89Magic)),
            ".webp" => span.Length >= 12 && span[..4].SequenceEqual(RiffMagic) && span[8..12].SequenceEqual(WebpMagic),
            ".ico" => span.Length >= IcoMagic.Length && span.StartsWith(IcoMagic),
            _ => false
        };

        if (valid)
        {
            return BinaryValidationResult.Valid();
        }

        return BinaryValidationResult.Invalid(
            $"Header bytes do not match expected magic signature for {context.Extension}.",
            DiffRiskSeverity.Critical,
            "PAY-BIN-002");
    }
}

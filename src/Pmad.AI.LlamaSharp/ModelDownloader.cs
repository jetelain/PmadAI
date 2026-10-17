using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Pmad.AI.LlamaSharp;

/// <summary>
/// Represents the progress of a model or projector file download operation.
/// </summary>
/// <param name="BytesDownloaded">Total number of bytes transferred so far.</param>
/// <param name="TotalBytes">Total content length in bytes, or null if unknown.</param>
/// <param name="FileName">Name of the target file being downloaded, if available.</param>
public readonly record struct ModelDownloadProgress(
    long BytesDownloaded,
    long? TotalBytes,
    string? FileName = null)
{
    /// <summary>
    /// Gets the completion percentage from 0.0 to 100.0, or null if total size is unknown.
    /// </summary>
    public double? ProgressPercentage => TotalBytes.HasValue && TotalBytes.Value > 0
        ? (double)BytesDownloaded / TotalBytes.Value * 100.0
        : null;

    /// <summary>
    /// Gets the completion fraction from 0.0 to 1.0, or null if total size is unknown.
    /// </summary>
    public double? ProgressFraction => TotalBytes.HasValue && TotalBytes.Value > 0
        ? (double)BytesDownloaded / TotalBytes.Value
        : null;
}

/// <summary>
/// Downloads single-file GGUF models and vision projectors from remote HTTP endpoints into local model cache.
/// </summary>
public sealed class ModelDownloader
{
    private readonly HttpClient _http;
    private readonly ILogger? _logger;
    private readonly Action<string>? _statusLogger;
    private readonly IProgress<ModelDownloadProgress>? _progress;

    /// <summary>
    /// Initializes a new instance of <see cref="ModelDownloader"/>.
    /// </summary>
    /// <param name="http">Optional custom <see cref="HttpClient"/>.</param>
    /// <param name="logger">Optional <see cref="ILogger"/> for structured logging.</param>
    /// <param name="statusLogger">Optional default status logger action invoked for progress text messages.</param>
    /// <param name="progress">Optional default <see cref="IProgress{T}"/> reporter for transfer progress.</param>
    public ModelDownloader(
        HttpClient? http = null,
        ILogger? logger = null,
        Action<string>? statusLogger = null,
        IProgress<ModelDownloadProgress>? progress = null)
    {
        _http = http ?? new HttpClient();
        _logger = logger;
        _statusLogger = statusLogger;
        _progress = progress;
        try
        {
            if (!_http.DefaultRequestHeaders.UserAgent.Any())
            {
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("Pmad.AI.LlamaSharp/1.0 (Local GGUF Engine)");
            }
        }
        catch (InvalidOperationException)
        {
            // HttpClient may already have initiated requests; DefaultRequestHeaders is read-only in that state
        }
    }

    /// <summary>
    /// Downloads a preset model into the specified models cache directory if not already present.
    /// Also downloads the companion vision projector (<c>mmproj-*.gguf</c>) when available and <paramref name="downloadVision"/> is true.
    /// Also downloads the companion Multi-Token Prediction (MTP) draft model (<c>mtp-*.gguf</c>) when available and <paramref name="downloadMtp"/> is true.
    /// </summary>
    public async Task<bool> DownloadModelAsync(
        LlamaModelPreset preset,
        string destinationDir,
        bool downloadVision = true,
        bool downloadMtp = true,
        Action<string>? statusLogger = null,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        Directory.CreateDirectory(destinationDir);

        var targetFile = preset.GetLocalPath(destinationDir);
        var downloadUrl = preset.DownloadUrl;

        LogStatus($"Preparing GGUF model '{preset.Id}' from {downloadUrl}...", statusLogger);
        var success = await DownloadFileWithProgressAsync(downloadUrl, targetFile, statusLogger, progress, ct).ConfigureAwait(false);
        if (!success) return false;

        if (downloadVision && preset.HasVision && !string.IsNullOrEmpty(preset.MmprojDownloadUrl))
        {
            var mmprojTarget = preset.GetMmprojLocalPath(destinationDir)!;
            LogStatus($"Preparing vision projector for '{preset.Id}' from {preset.MmprojDownloadUrl}...", statusLogger);
            var visionSuccess = await DownloadFileWithProgressAsync(preset.MmprojDownloadUrl, mmprojTarget, statusLogger, progress, ct).ConfigureAwait(false);
            if (!visionSuccess) return false;
        }

        if (downloadMtp && preset.HasMtp && !string.IsNullOrEmpty(preset.MtpDownloadUrl))
        {
            var mtpTarget = preset.GetMtpLocalPath(destinationDir)!;
            LogStatus($"Preparing MTP draft model for '{preset.Id}' from {preset.MtpDownloadUrl}...", statusLogger);
            var mtpSuccess = await DownloadFileWithProgressAsync(preset.MtpDownloadUrl, mtpTarget, statusLogger, progress, ct).ConfigureAwait(false);
            if (!mtpSuccess) return false;
        }

        return true;
    }

    /// <summary>
    /// Downloads a preset model into the specified models cache directory if not already present.
    /// Also downloads the companion vision projector (<c>mmproj-*.gguf</c>) when available and <paramref name="downloadVision"/> is true.
    /// </summary>
    public Task<bool> DownloadModelAsync(
        LlamaModelPreset preset,
        string destinationDir,
        bool downloadVision,
        Action<string>? statusLogger,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default)
        => DownloadModelAsync(preset, destinationDir, downloadVision, downloadMtp: true, statusLogger, progress, ct);

    /// <summary>
    /// Streams a remote file to a local destination using a .part temporary file,
    /// skipping if the file already exists with a matching Content-Length.
    /// </summary>
    public async Task<bool> DownloadFileWithProgressAsync(
        string fileUrl,
        string localPath,
        Action<string>? statusLogger = null,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var localDir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(localDir))
        {
            Directory.CreateDirectory(localDir);
        }

        var fileName = Path.GetFileName(localPath);
        var effectiveProgress = progress ?? _progress;

        long? totalBytes = null;
        try
        {
            using var headReq = new HttpRequestMessage(HttpMethod.Head, fileUrl);
            using var headRes = await _http.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (headRes.IsSuccessStatusCode)
            {
                totalBytes = headRes.Content.Headers.ContentLength;
            }
        }
        catch
        {
            // HEAD request is optional; server may not support HEAD
        }

        if (File.Exists(localPath) && totalBytes.HasValue && new FileInfo(localPath).Length == totalBytes.Value)
        {
            LogStatus($"  -> Already downloaded ({FormatBytes(totalBytes.Value)}), skipping.", statusLogger);
            effectiveProgress?.Report(new ModelDownloadProgress(totalBytes.Value, totalBytes.Value, fileName));
            return true;
        }

        var partPath = localPath + ".part";
        if (File.Exists(partPath))
        {
            File.Delete(partPath);
        }

        using var getRes = await _http.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        getRes.EnsureSuccessStatusCode();

        totalBytes ??= getRes.Content.Headers.ContentLength;

        if (File.Exists(localPath) && totalBytes.HasValue && new FileInfo(localPath).Length == totalBytes.Value)
        {
            LogStatus($"  -> Already downloaded ({FormatBytes(totalBytes.Value)}), skipping.", statusLogger);
            effectiveProgress?.Report(new ModelDownloadProgress(totalBytes.Value, totalBytes.Value, fileName));
            return true;
        }

        var sizeStr = totalBytes.HasValue ? $" ({FormatBytes(totalBytes.Value)})" : "";
        LogStatus($"  -> Fetching{sizeStr}...", statusLogger);
        effectiveProgress?.Report(new ModelDownloadProgress(0, totalBytes, fileName));

        await using var contentStream = await getRes.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long downloaded = 0;
        var sw = Stopwatch.StartNew();
        var progressSw = Stopwatch.StartNew();
        long lastReportedBytes = 0;
        long lastProgressBytes = 0;
        int bytesRead;

        try
        {
            while ((bytesRead = await contentStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                downloaded += bytesRead;

                if (progressSw.ElapsedMilliseconds >= 250 || downloaded - lastProgressBytes >= 5 * 1024 * 1024)
                {
                    effectiveProgress?.Report(new ModelDownloadProgress(downloaded, totalBytes, fileName));
                    lastProgressBytes = downloaded;
                    progressSw.Restart();
                }

                if (downloaded - lastReportedBytes >= 50 * 1024 * 1024 || sw.ElapsedMilliseconds >= 3000)
                {
                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        var pct = (double)downloaded / totalBytes.Value * 100.0;
                        LogStatus($"     {FormatBytes(downloaded)} / {FormatBytes(totalBytes.Value)} ({pct:F1}%)", statusLogger);
                    }
                    else
                    {
                        LogStatus($"     {FormatBytes(downloaded)} downloaded", statusLogger);
                    }
                    lastReportedBytes = downloaded;
                    sw.Restart();
                }
            }

            await fileStream.FlushAsync(ct).ConfigureAwait(false);
            fileStream.Close();
        }
        catch
        {
            try
            {
                fileStream.Close();
                if (File.Exists(partPath))
                {
                    File.Delete(partPath);
                }
            }
            catch
            {
                // Suppress secondary cleanup exceptions
            }
            throw;
        }

        File.Move(partPath, localPath, overwrite: true);

        effectiveProgress?.Report(new ModelDownloadProgress(downloaded, totalBytes ?? downloaded, fileName));
        LogStatus($"  -> Model saved: {fileName} ({FormatBytes(downloaded)})", statusLogger);
        return true;
    }

    private void LogStatus(string message, Action<string>? overrideStatusLogger)
    {
        (overrideStatusLogger ?? _statusLogger)?.Invoke(message);
        _logger?.LogInformation("{Status}", message);
    }

    /// <summary>
    /// Formats a byte count into a human-readable string (e.g. "1.2 GB").
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int i = 0;
        double d = bytes;
        while (d >= 1024 && i < suffixes.Length - 1)
        {
            d /= 1024;
            i++;
        }
        return $"{d.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} {suffixes[i]}";
    }
}
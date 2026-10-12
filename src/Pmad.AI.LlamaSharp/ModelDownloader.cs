using System.Diagnostics;

namespace Pmad.AI.LlamaSharp;

/// <summary>
/// Downloads single-file GGUF models and vision projectors from remote HTTP endpoints into local model cache.
/// </summary>
public sealed class ModelDownloader
{
    private readonly HttpClient _http;

    /// <summary>
    /// Initializes a new instance of <see cref="ModelDownloader"/>.
    /// </summary>
    public ModelDownloader(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Pmad.AI.LlamaSharp/1.0 (Local GGUF Engine)");
        }
    }

    /// <summary>
    /// Downloads a preset model into the specified models cache directory if not already present.
    /// Also downloads the companion vision projector (<c>mmproj-*.gguf</c>) when available and <paramref name="downloadVision"/> is true.
    /// </summary>
    public async Task<bool> DownloadModelAsync(
        LlamaModelPreset preset,
        string destinationDir,
        bool downloadVision = true,
        Action<string>? statusLogger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        Directory.CreateDirectory(destinationDir);

        var targetFile = preset.GetLocalPath(destinationDir);
        var downloadUrl = preset.DownloadUrl;

        statusLogger?.Invoke($"Preparing GGUF model '{preset.Id}' from {downloadUrl}...");
        var success = await DownloadFileWithProgressAsync(downloadUrl, targetFile, statusLogger, ct).ConfigureAwait(false);
        if (!success) return false;

        if (downloadVision && preset.HasVision && !string.IsNullOrEmpty(preset.MmprojDownloadUrl))
        {
            var mmprojTarget = preset.GetMmprojLocalPath(destinationDir)!;
            statusLogger?.Invoke($"Preparing vision projector for '{preset.Id}' from {preset.MmprojDownloadUrl}...");
            await DownloadFileWithProgressAsync(preset.MmprojDownloadUrl, mmprojTarget, statusLogger, ct).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Streams a remote file to a local destination using a .part temporary file,
    /// skipping if the file already exists with a matching Content-Length.
    /// </summary>
    public async Task<bool> DownloadFileWithProgressAsync(
        string fileUrl,
        string localPath,
        Action<string>? statusLogger = null,
        CancellationToken ct = default)
    {
        var localDir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(localDir))
        {
            Directory.CreateDirectory(localDir);
        }

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
            statusLogger?.Invoke($"  -> Already downloaded ({FormatBytes(totalBytes.Value)}), skipping.");
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

        var sizeStr = totalBytes.HasValue ? $" ({FormatBytes(totalBytes.Value)})" : "";
        statusLogger?.Invoke($"  -> Fetching{sizeStr}...");

        await using var contentStream = await getRes.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long downloaded = 0;
        var sw = Stopwatch.StartNew();
        long lastReportedBytes = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
            downloaded += bytesRead;

            if (downloaded - lastReportedBytes >= 50 * 1024 * 1024 || sw.ElapsedMilliseconds >= 3000)
            {
                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    var pct = (double)downloaded / totalBytes.Value * 100.0;
                    statusLogger?.Invoke($"     {FormatBytes(downloaded)} / {FormatBytes(totalBytes.Value)} ({pct:F1}%)");
                }
                else
                {
                    statusLogger?.Invoke($"     {FormatBytes(downloaded)} downloaded");
                }
                lastReportedBytes = downloaded;
                sw.Restart();
            }
        }

        await fileStream.FlushAsync(ct).ConfigureAwait(false);
        fileStream.Close();

        if (File.Exists(localPath))
        {
            File.Delete(localPath);
        }
        File.Move(partPath, localPath);

        statusLogger?.Invoke($"  -> Model saved: {Path.GetFileName(localPath)} ({FormatBytes(downloaded)})");
        return true;
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
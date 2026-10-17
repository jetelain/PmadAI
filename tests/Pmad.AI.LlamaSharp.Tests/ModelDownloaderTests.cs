using System.Net;
using System.Text;
using Pmad.AI.LlamaSharp;

namespace Pmad.AI.LlamaSharp.Tests;

public class ModelDownloaderTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Theory]
    [InlineData(0, "0.0 B")]
    [InlineData(512, "512.0 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1073741824, "1.0 GB")]
    [InlineData(2684354560, "2.5 GB")]
    [InlineData(1099511627776, "1.0 TB")]
    public void FormatBytes_FormatsCorrectly(long bytes, string expected)
    {
        var formatted = ModelDownloader.FormatBytes(bytes);
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public async Task DownloadFileWithProgressAsync_DownloadsFileSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test_download_" + Guid.NewGuid().ToString("N"));
        var targetFile = Path.Combine(tempDir, "model.gguf");
        var fileContent = Encoding.UTF8.GetBytes("GGUF_TEST_FILE_CONTENT_1234567890");

        var logs = new List<string>();

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Head)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent([]);
                response.Content.Headers.ContentLength = fileContent.Length;
                return response;
            }
            if (req.Method == HttpMethod.Get)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(fileContent)
                };
                response.Content.Headers.ContentLength = fileContent.Length;
                return response;
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        var downloader = new ModelDownloader(client);

        try
        {
            var success = await downloader.DownloadFileWithProgressAsync(
                "https://example.com/model.gguf",
                targetFile,
                statusLogger: logs.Add);

            Assert.True(success);
            Assert.True(File.Exists(targetFile));
            var downloadedBytes = await File.ReadAllBytesAsync(targetFile);
            Assert.Equal(fileContent, downloadedBytes);
            Assert.False(File.Exists(targetFile + ".part"));
            Assert.Contains(logs, l => l.Contains("Fetching"));
            Assert.Contains(logs, l => l.Contains("Model saved"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadFileWithProgressAsync_WhenFileAlreadyExists_SkipsDownload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test_download_" + Guid.NewGuid().ToString("N"));
        var targetFile = Path.Combine(tempDir, "existing.gguf");
        var fileContent = Encoding.UTF8.GetBytes("EXISTING_CONTENT");

        Directory.CreateDirectory(tempDir);
        await File.WriteAllBytesAsync(targetFile, fileContent);

        var logs = new List<string>();
        var getCalled = false;

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Head)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent([]);
                response.Content.Headers.ContentLength = fileContent.Length;
                return response;
            }
            if (req.Method == HttpMethod.Get)
            {
                getCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(fileContent)
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        var downloader = new ModelDownloader(client);

        try
        {
            var success = await downloader.DownloadFileWithProgressAsync(
                "https://example.com/existing.gguf",
                targetFile,
                statusLogger: logs.Add);

            Assert.True(success);
            Assert.False(getCalled);
            Assert.Contains(logs, l => l.Contains("Already downloaded"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadModelAsync_WithVision_DownloadsBothModelAndMmproj()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test_download_" + Guid.NewGuid().ToString("N"));
        var preset = LlamaModelPreset.Default;

        var modelData = Encoding.UTF8.GetBytes("MODEL_DUMMY_DATA");
        var mmprojData = Encoding.UTF8.GetBytes("MMPROJ_DUMMY_DATA");

        var requestedUrls = new List<string>();

        var handler = new MockHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            requestedUrls.Add(url);

            var data = url.Contains("mmproj") ? mmprojData : modelData;

            if (req.Method == HttpMethod.Head)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent([]);
                response.Content.Headers.ContentLength = data.Length;
                return response;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(data)
            };
        });

        using var client = new HttpClient(handler);
        var downloader = new ModelDownloader(client);

        try
        {
            var success = await downloader.DownloadModelAsync(
                preset,
                tempDir,
                downloadVision: true);

            Assert.True(success);

            var targetModelFile = preset.GetLocalPath(tempDir);
            var targetMmprojFile = preset.GetMmprojLocalPath(tempDir);

            Assert.True(File.Exists(targetModelFile));
            Assert.NotNull(targetMmprojFile);
            Assert.True(File.Exists(targetMmprojFile));

            Assert.Contains(requestedUrls, u => u == preset.DownloadUrl);
            Assert.Contains(requestedUrls, u => u == preset.MmprojDownloadUrl);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadModelAsync_WithoutVision_OnlyDownloadsModel()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test_download_" + Guid.NewGuid().ToString("N"));
        var preset = LlamaModelPreset.Default;

        var modelData = Encoding.UTF8.GetBytes("MODEL_DUMMY_DATA");
        var requestedUrls = new List<string>();

        var handler = new MockHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            requestedUrls.Add(url);

            if (req.Method == HttpMethod.Head)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent([]);
                response.Content.Headers.ContentLength = modelData.Length;
                return response;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(modelData)
            };
        });

        using var client = new HttpClient(handler);
        var downloader = new ModelDownloader(client);

        try
        {
            var success = await downloader.DownloadModelAsync(
                preset,
                tempDir,
                downloadVision: false);

            Assert.True(success);

            var targetModelFile = preset.GetLocalPath(tempDir);
            var targetMmprojFile = preset.GetMmprojLocalPath(tempDir);

            Assert.True(File.Exists(targetModelFile));
            Assert.NotNull(targetMmprojFile);
            Assert.False(File.Exists(targetMmprojFile));

            Assert.Contains(requestedUrls, u => u == preset.DownloadUrl);
            Assert.DoesNotContain(requestedUrls, u => u == preset.MmprojDownloadUrl);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadModelAsync_WithConstructorLoggerAndProgress_ReportsAll()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test_download_" + Guid.NewGuid().ToString("N"));
        var preset = LlamaModelPreset.Default;
        var modelData = Encoding.UTF8.GetBytes("MODEL_TEST_DATA");

        var constructorLogs = new List<string>();
        var progressUpdates = new List<ModelDownloadProgress>();
        var progressReporter = new Progress<ModelDownloadProgress>(progressUpdates.Add);

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Head)
            {
                var headRes = new HttpResponseMessage(HttpStatusCode.OK);
                headRes.Content = new ByteArrayContent([]);
                headRes.Content.Headers.ContentLength = modelData.Length;
                return headRes;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(modelData)
            };
        });

        using var client = new HttpClient(handler);
        var downloader = new ModelDownloader(
            http: client,
            logger: null,
            statusLogger: constructorLogs.Add,
            progress: progressReporter);

        try
        {
            var success = await downloader.DownloadModelAsync(
                preset,
                tempDir,
                downloadVision: false);

            Assert.True(success);
            Assert.NotEmpty(constructorLogs);
            Assert.Contains(constructorLogs, l => l.Contains("Preparing GGUF model"));
            Assert.Contains(constructorLogs, l => l.Contains("Model saved"));

            Assert.NotEmpty(progressUpdates);
            var lastProgress = progressUpdates[^1];
            Assert.Equal(modelData.Length, lastProgress.BytesDownloaded);
            Assert.Equal(modelData.Length, lastProgress.TotalBytes);
            Assert.Equal(100.0, lastProgress.ProgressPercentage);
            Assert.Equal(1.0, lastProgress.ProgressFraction);
            Assert.Equal(preset.FileName, lastProgress.FileName);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void ModelDownloadProgress_CalculatesPercentageAndFractionCorrectly()
    {
        var progress1 = new ModelDownloadProgress(500, 1000, "test.gguf");
        Assert.Equal(50.0, progress1.ProgressPercentage);
        Assert.Equal(0.5, progress1.ProgressFraction);
        Assert.Equal("test.gguf", progress1.FileName);

        var progressUnknown = new ModelDownloadProgress(500, null, "test.gguf");
        Assert.Null(progressUnknown.ProgressPercentage);
        Assert.Null(progressUnknown.ProgressFraction);

        var progressZero = new ModelDownloadProgress(0, 0, "empty.gguf");
        Assert.Null(progressZero.ProgressPercentage);
        Assert.Null(progressZero.ProgressFraction);
    }

    [Fact]
    public async Task ModelDownloader_WithActiveHttpClient_DoesNotThrowOnConstructor()
    {
        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler);

        // Make an initial request to put client into active/started state
        await client.GetAsync("https://example.com/init");

        // Should not throw InvalidOperationException when instantiated with an already active client
        var downloader = new ModelDownloader(client);
        Assert.NotNull(downloader);
    }

    [Fact]
    public async Task DownloadFileWithProgressAsync_WhenHeadFails_ButGetMatchesLength_SkipsDownload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test_download_" + Guid.NewGuid().ToString("N"));
        var targetFile = Path.Combine(tempDir, "existing_head_fail.gguf");
        var fileContent = Encoding.UTF8.GetBytes("EXISTING_CONTENT_FOR_HEAD_FAIL_TEST");

        Directory.CreateDirectory(tempDir);
        await File.WriteAllBytesAsync(targetFile, fileContent);

        var logs = new List<string>();
        var getStreamRead = false;

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Head)
            {
                // Simulate server that does not support HEAD
                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            }
            if (req.Method == HttpMethod.Get)
            {
                var stream = new MemoryStream(fileContent);
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(stream)
                };
                response.Content.Headers.ContentLength = fileContent.Length;
                getStreamRead = true;
                return response;
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        var downloader = new ModelDownloader(client);

        try
        {
            var success = await downloader.DownloadFileWithProgressAsync(
                "https://example.com/existing_head_fail.gguf",
                targetFile,
                statusLogger: logs.Add);

            Assert.True(success);
            Assert.True(getStreamRead);
            Assert.Contains(logs, l => l.Contains("Already downloaded"));
            // Shouldn't create .part file
            Assert.False(File.Exists(targetFile + ".part"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadModelAsync_WhenMmprojDownloadFails_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test_download_" + Guid.NewGuid().ToString("N"));
        var preset = LlamaModelPreset.Default;
        var modelData = Encoding.UTF8.GetBytes("MODEL_DATA");

        var handler = new MockHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("mmproj"))
            {
                // Projector download fails
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (req.Method == HttpMethod.Head)
            {
                var res = new HttpResponseMessage(HttpStatusCode.OK);
                res.Content = new ByteArrayContent([]);
                res.Content.Headers.ContentLength = modelData.Length;
                return res;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(modelData)
            };
        });

        using var client = new HttpClient(handler);
        var downloader = new ModelDownloader(client);

        try
        {
            await Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                await downloader.DownloadModelAsync(preset, tempDir, downloadVision: true);
            });
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadModelAsync_WithMtp_DownloadsBothModelAndMtp()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test_download_" + Guid.NewGuid().ToString("N"));
        var preset = LlamaModelPreset.Find("gemma-4-12b")!;
        Assert.True(preset.HasMtp);

        var modelData = Encoding.UTF8.GetBytes("MODEL_DATA");
        var mmprojData = Encoding.UTF8.GetBytes("MMPROJ_DATA");
        var mtpData = Encoding.UTF8.GetBytes("MTP_DATA");

        var requestedUrls = new List<string>();

        var handler = new MockHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            requestedUrls.Add(url);

            byte[] data;
            if (url.Contains("mtp"))
            {
                data = mtpData;
            }
            else if (url.Contains("mmproj"))
            {
                data = mmprojData;
            }
            else
            {
                data = modelData;
            }

            if (req.Method == HttpMethod.Head)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent([]);
                response.Content.Headers.ContentLength = data.Length;
                return response;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(data)
            };
        });

        using var client = new HttpClient(handler);
        var downloader = new ModelDownloader(client);

        try
        {
            var success = await downloader.DownloadModelAsync(
                preset,
                tempDir,
                downloadVision: true,
                downloadMtp: true);

            Assert.True(success);

            var targetModelFile = preset.GetLocalPath(tempDir);
            var targetMmprojFile = preset.GetMmprojLocalPath(tempDir);
            var targetMtpFile = preset.GetMtpLocalPath(tempDir);

            Assert.True(File.Exists(targetModelFile));
            Assert.NotNull(targetMmprojFile);
            Assert.True(File.Exists(targetMmprojFile));
            Assert.NotNull(targetMtpFile);
            Assert.True(File.Exists(targetMtpFile));

            Assert.Contains(requestedUrls, u => u == preset.DownloadUrl);
            Assert.Contains(requestedUrls, u => u == preset.MmprojDownloadUrl);
            Assert.Contains(requestedUrls, u => u == preset.MtpDownloadUrl);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadModelAsync_WithoutMtp_SkipsMtpDownload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test_download_" + Guid.NewGuid().ToString("N"));
        var preset = LlamaModelPreset.Find("gemma-4-12b")!;
        Assert.True(preset.HasMtp);

        var modelData = Encoding.UTF8.GetBytes("MODEL_DATA");
        var requestedUrls = new List<string>();

        var handler = new MockHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            requestedUrls.Add(url);

            if (req.Method == HttpMethod.Head)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent([]);
                response.Content.Headers.ContentLength = modelData.Length;
                return response;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(modelData)
            };
        });

        using var client = new HttpClient(handler);
        var downloader = new ModelDownloader(client);

        try
        {
            var success = await downloader.DownloadModelAsync(
                preset,
                tempDir,
                downloadVision: false,
                downloadMtp: false);

            Assert.True(success);

            var targetModelFile = preset.GetLocalPath(tempDir);
            var targetMtpFile = preset.GetMtpLocalPath(tempDir);

            Assert.True(File.Exists(targetModelFile));
            Assert.NotNull(targetMtpFile);
            Assert.False(File.Exists(targetMtpFile));

            Assert.Contains(requestedUrls, u => u == preset.DownloadUrl);
            Assert.DoesNotContain(requestedUrls, u => u == preset.MtpDownloadUrl);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadModelAsync_WhenMtpDownloadFails_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test_download_" + Guid.NewGuid().ToString("N"));
        var preset = LlamaModelPreset.Find("gemma-4-e2b")!;

        var modelData = Encoding.UTF8.GetBytes("MODEL_DATA");

        var handler = new MockHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("mtp"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (req.Method == HttpMethod.Head)
            {
                var res = new HttpResponseMessage(HttpStatusCode.OK);
                res.Content = new ByteArrayContent([]);
                res.Content.Headers.ContentLength = modelData.Length;
                return res;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(modelData)
            };
        });

        using var client = new HttpClient(handler);
        var downloader = new ModelDownloader(client);

        try
        {
            await Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                await downloader.DownloadModelAsync(preset, tempDir, downloadVision: false, downloadMtp: true);
            });
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}


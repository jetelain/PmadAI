using Pmad.AI.LlamaSharp;

namespace Pmad.AI.LlamaSharp.Tests;

public class LlamaModelPresetTests
{
    [Fact]
    public void Presets_ContainsDefaultPresets()
    {
        Assert.NotEmpty(LlamaModelPreset.Presets);
        Assert.Equal(6, LlamaModelPreset.Presets.Length);
        Assert.Equal("qwen3.5-2b", LlamaModelPreset.Default.Id);
    }

    [Fact]
    public void Presets_HaveConfiguredTokenEstimators()
    {
        foreach (var preset in LlamaModelPreset.Presets)
        {
            Assert.NotNull(preset.TokenEstimator);
            if (preset.Id.StartsWith("qwen"))
            {
                Assert.Same(Pmad.AI.Tokens.HeuristicTokenEstimator.Qwen, preset.TokenEstimator);
            }
            else if (preset.Id.StartsWith("gemma"))
            {
                Assert.Same(Pmad.AI.Tokens.HeuristicTokenEstimator.Gemma, preset.TokenEstimator);
            }
        }
    }

    [Theory]
    [InlineData("qwen3.5-2b", "qwen3.5-2b")]
    [InlineData("QWEN3.5-2B", "qwen3.5-2b")]
    [InlineData("qwen3.5-4b", "qwen3.5-4b")]
    [InlineData("qwen3.5-9b", "qwen3.5-9b")]
    [InlineData("gemma-4-e2b", "gemma-4-e2b")]
    [InlineData("gemma-4-e4b", "gemma-4-e4b")]
    [InlineData("gemma-4-12b", "gemma-4-12b")]
    public void Find_ById_ReturnsExpectedPreset(string query, string expectedId)
    {
        var preset = LlamaModelPreset.Find(query);
        Assert.NotNull(preset);
        Assert.Equal(expectedId, preset.Id);
    }

    [Theory]
    [InlineData("qwen3.5", "qwen3.5-2b")]
    [InlineData("qwen3.5-2b-gguf", "qwen3.5-2b")]
    [InlineData("qwen3.5-4b-q4", "qwen3.5-4b")]
    [InlineData("qwen3.5-9b-q4", "qwen3.5-9b")]
    [InlineData("qwen-9b", "qwen3.5-9b")]
    [InlineData("qwen3.5-9b-it", "qwen3.5-9b")]
    [InlineData("gemma4-e2b", "gemma-4-e2b")]
    [InlineData("gemma-4-e2b-it", "gemma-4-e2b")]
    [InlineData("gemma4", "gemma-4-e2b")]
    [InlineData("gemma-4", "gemma-4-e2b")]
    [InlineData("gemma4-e4b", "gemma-4-e4b")]
    [InlineData("gemma-4b", "gemma-4-e4b")]
    [InlineData("gemma4-12b", "gemma-4-12b")]
    [InlineData("gemma-12b", "gemma-4-12b")]
    [InlineData("gemma-4-12b-it", "gemma-4-12b")]
    public void Find_ByAlias_ReturnsExpectedPreset(string query, string expectedId)
    {
        var preset = LlamaModelPreset.Find(query);
        Assert.NotNull(preset);
        Assert.Equal(expectedId, preset.Id);
    }

    [Fact]
    public void Find_ByFileNameOrName_ReturnsPreset()
    {
        var byFileName = LlamaModelPreset.Find("qwen3.5-2b-ud-q4_k_xl.gguf");
        Assert.NotNull(byFileName);
        Assert.Equal("qwen3.5-2b", byFileName.Id);

        var qwen9bByFileName = LlamaModelPreset.Find("qwen3.5-9b-ud-q4_k_xl.gguf");
        Assert.NotNull(qwen9bByFileName);
        Assert.Equal("qwen3.5-9b", qwen9bByFileName.Id);

        var gemma12bByFileName = LlamaModelPreset.Find("gemma-4-12b-it-ud-q4_k_xl.gguf");
        Assert.NotNull(gemma12bByFileName);
        Assert.Equal("gemma-4-12b", gemma12bByFileName.Id);

        var byFullName = LlamaModelPreset.Find("Gemma 4 E2B (Q4_K_XL)");
        Assert.NotNull(byFullName);
        Assert.Equal("gemma-4-e2b", byFullName.Id);

        var gemma12bByName = LlamaModelPreset.Find("Gemma 4 12B (Q4_K_XL)");
        Assert.NotNull(gemma12bByName);
        Assert.Equal("gemma-4-12b", gemma12bByName.Id);
    }

    [Theory]
    [InlineData("mtp-gemma-4-e2b-it.gguf", "gemma-4-e2b")]
    [InlineData("mtp-gemma-4-e4b-it.gguf", "gemma-4-e4b")]
    [InlineData("mtp-gemma-4-12b-it.gguf", "gemma-4-12b")]
    public void Find_ByMtpFileName_ReturnsExpectedPreset(string mtpFileName, string expectedId)
    {
        var preset = LlamaModelPreset.Find(mtpFileName);
        Assert.NotNull(preset);
        Assert.Equal(expectedId, preset.Id);
    }

    [Fact]
    public void Find_QwenPrefixFallback_ReturnsDefault()
    {
        var preset = LlamaModelPreset.Find("qwen-custom-future-variant");
        Assert.NotNull(preset);
        Assert.Equal(LlamaModelPreset.Default.Id, preset.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("non-existent-unknown-preset")]
    public void Find_UnknownOrEmpty_ReturnsNull(string? query)
    {
        var preset = LlamaModelPreset.Find(query);
        Assert.Null(preset);
    }

    [Fact]
    public void DownloadUrl_DefaultHostingBase_GeneratesCorrectUrl()
    {
        var preset = LlamaModelPreset.Default;
        var url = preset.DownloadUrl;

        Assert.Equal("https://onnx.pmad.net/qwen3.5-2b/qwen3.5-2b-ud-q4_k_xl.gguf", url);
    }

    [Fact]
    public void MmprojDownloadUrl_WhenHasVision_GeneratesCorrectUrl()
    {
        var preset = LlamaModelPreset.Default;

        Assert.True(preset.HasVision);
        Assert.Equal("https://onnx.pmad.net/qwen3.5-2b/mmproj-bf16.gguf", preset.MmprojDownloadUrl);
    }

    [Fact]
    public void MmprojDownloadUrl_WhenVisionDisabled_ReturnsNull()
    {
        var preset = new LlamaModelPreset(
            Id: "custom-text-only",
            Name: "Custom Text Only",
            FileName: "model.gguf",
            ApproxSize: "1 GB",
            Description: "No vision model",
            MmprojFileName: null);

        Assert.False(preset.HasVision);
        Assert.Null(preset.MmprojDownloadUrl);
        Assert.Null(preset.GetMmprojLocalPath("/tmp/models"));
    }

    [Fact]
    public void MtpDownloadUrl_WhenHasMtp_GeneratesCorrectUrl()
    {
        var gemma12b = LlamaModelPreset.Find("gemma-4-12b");
        Assert.NotNull(gemma12b);
        Assert.True(gemma12b.HasMtp);
        Assert.Equal("https://onnx.pmad.net/gemma-4-12b/mtp-gemma-4-12b-it.gguf", gemma12b.MtpDownloadUrl);

        var gemma2b = LlamaModelPreset.Find("gemma-4-e2b");
        Assert.NotNull(gemma2b);
        Assert.True(gemma2b.HasMtp);
        Assert.Equal("https://onnx.pmad.net/gemma-4-e2b/mtp-gemma-4-e2b-it.gguf", gemma2b.MtpDownloadUrl);

        var gemma4b = LlamaModelPreset.Find("gemma-4-e4b");
        Assert.NotNull(gemma4b);
        Assert.True(gemma4b.HasMtp);
        Assert.Equal("https://onnx.pmad.net/gemma-4-e4b/mtp-gemma-4-e4b-it.gguf", gemma4b.MtpDownloadUrl);
    }

    [Fact]
    public void MtpDownloadUrl_WhenMtpNotSupported_ReturnsNull()
    {
        var qwen2b = LlamaModelPreset.Find("qwen3.5-2b");
        Assert.NotNull(qwen2b);
        Assert.False(qwen2b.HasMtp);
        Assert.Null(qwen2b.MtpFileName);
        Assert.Null(qwen2b.MtpDownloadUrl);
        Assert.Null(qwen2b.GetMtpLocalPath("/tmp/models"));

        var qwen9b = LlamaModelPreset.Find("qwen3.5-9b");
        Assert.NotNull(qwen9b);
        Assert.False(qwen9b.HasMtp);
        Assert.Null(qwen9b.MtpFileName);
        Assert.Null(qwen9b.MtpDownloadUrl);
        Assert.Null(qwen9b.GetMtpLocalPath("/tmp/models"));
    }

    [Fact]
    public void ResolvedBaseUrl_CustomBaseUrlWithOrWithoutTrailingSlash()
    {
        var presetWithSlash = new LlamaModelPreset(
            Id: "custom",
            Name: "Custom",
            FileName: "test.gguf",
            ApproxSize: "1 GB",
            Description: "Test",
            BaseUrl: "https://mycdn.example.com/models/");

        Assert.Equal("https://mycdn.example.com/models/", presetWithSlash.ResolvedBaseUrl);
        Assert.Equal("https://mycdn.example.com/models/test.gguf", presetWithSlash.DownloadUrl);

        var presetWithoutSlash = new LlamaModelPreset(
            Id: "custom",
            Name: "Custom",
            FileName: "test.gguf",
            ApproxSize: "1 GB",
            Description: "Test",
            BaseUrl: "https://mycdn.example.com/models");

        Assert.Equal("https://mycdn.example.com/models/", presetWithoutSlash.ResolvedBaseUrl);
        Assert.Equal("https://mycdn.example.com/models/test.gguf", presetWithoutSlash.DownloadUrl);
    }

    [Fact]
    public void GetLocalPath_CombinesBaseDirAndFileName()
    {
        var preset = LlamaModelPreset.Default;
        var baseDir = Path.Combine(Path.GetTempPath(), "models_test");

        var localPath = preset.GetLocalPath(baseDir);
        var expected = Path.Combine(baseDir, preset.FileName);

        Assert.Equal(expected, localPath);
    }

    [Fact]
    public void GetMmprojLocalPath_CombinesBaseDirPresetIdAndMmprojFileName()
    {
        var preset = LlamaModelPreset.Default;
        var baseDir = Path.Combine(Path.GetTempPath(), "models_test");

        var mmprojPath = preset.GetMmprojLocalPath(baseDir);
        var expected = Path.Combine(baseDir, $"{preset.Id}-{preset.MmprojFileName}");

        Assert.Equal(expected, mmprojPath);
    }

    [Fact]
    public void GetMtpLocalPath_CombinesBaseDirAndMtpFileName()
    {
        var gemma12b = LlamaModelPreset.Find("gemma-4-12b")!;
        var baseDir = Path.Combine(Path.GetTempPath(), "models_test");

        var mtpPath = gemma12b.GetMtpLocalPath(baseDir);
        var expected = Path.Combine(baseDir, "mtp-gemma-4-12b-it.gguf");

        Assert.Equal(expected, mtpPath);
    }

    [Fact]
    public void Presets_ReasoningCapabilities_ConfiguredAsExpected()
    {
        var qwen2b = LlamaModelPreset.Find("qwen3.5-2b");
        Assert.NotNull(qwen2b);
        Assert.True(qwen2b.SupportsReasoning);
        Assert.False(qwen2b.SupportsReasoningEffort);

        var qwen4b = LlamaModelPreset.Find("qwen3.5-4b");
        Assert.NotNull(qwen4b);
        Assert.True(qwen4b.SupportsReasoning);
        Assert.False(qwen4b.SupportsReasoningEffort);

        var qwen9b = LlamaModelPreset.Find("qwen3.5-9b");
        Assert.NotNull(qwen9b);
        Assert.True(qwen9b.SupportsReasoning);
        Assert.False(qwen9b.SupportsReasoningEffort);

        var gemma2b = LlamaModelPreset.Find("gemma-4-e2b");
        Assert.NotNull(gemma2b);
        Assert.True(gemma2b.SupportsReasoning);
        Assert.False(gemma2b.SupportsReasoningEffort);

        var gemma4b = LlamaModelPreset.Find("gemma-4-e4b");
        Assert.NotNull(gemma4b);
        Assert.True(gemma4b.SupportsReasoning);
        Assert.False(gemma4b.SupportsReasoningEffort);

        var gemma12b = LlamaModelPreset.Find("gemma-4-12b");
        Assert.NotNull(gemma12b);
        Assert.True(gemma12b.SupportsReasoning);
        Assert.False(gemma12b.SupportsReasoningEffort);
    }
}

using Pmad.AI.LlamaSharp;

namespace Pmad.AI.LlamaSharp.Tests;

public class LlamaModelPresetTests
{
    [Fact]
    public void Presets_ContainsDefaultPresets()
    {
        Assert.NotEmpty(LlamaModelPreset.Presets);
        Assert.Equal("qwen3.5-2b", LlamaModelPreset.Default.Id);
    }

    [Theory]
    [InlineData("qwen3.5-2b", "qwen3.5-2b")]
    [InlineData("QWEN3.5-2B", "qwen3.5-2b")]
    [InlineData("qwen3.5-4b", "qwen3.5-4b")]
    [InlineData("gemma-4-e2b", "gemma-4-e2b")]
    [InlineData("gemma-4-e4b", "gemma-4-e4b")]
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
    [InlineData("gemma4-e2b", "gemma-4-e2b")]
    [InlineData("gemma-4-e2b-it", "gemma-4-e2b")]
    [InlineData("gemma4", "gemma-4-e2b")]
    [InlineData("gemma4-e4b", "gemma-4-e4b")]
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

        var byName = LlamaModelPreset.Find("Google Gemma 4 E2B instruct GGUF model");
        // name is "Gemma 4 E2B (Q4_K_XL)"
        var byFullName = LlamaModelPreset.Find("Gemma 4 E2B (Q4_K_XL)");
        Assert.NotNull(byFullName);
        Assert.Equal("gemma-4-e2b", byFullName.Id);
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
}

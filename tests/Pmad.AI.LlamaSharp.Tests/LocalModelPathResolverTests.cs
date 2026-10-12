using Pmad.AI.LlamaSharp;

namespace Pmad.AI.LlamaSharp.Tests;

public class LocalModelPathResolverTests
{
    [Fact]
    public void GetDefaultModelsDirectory_ReturnsValidDirectoryPath()
    {
        var defaultDir = LocalModelPathResolver.GetDefaultModelsDirectory();

        Assert.False(string.IsNullOrWhiteSpace(defaultDir));
        Assert.EndsWith("models", defaultDir);
    }

    [Fact]
    public void GetDefaultModelsDirectory_WithCustomAppName_IncludesCustomAppName()
    {
        var customDir = LocalModelPathResolver.GetDefaultModelsDirectory("customapp");

        Assert.False(string.IsNullOrWhiteSpace(customDir));
        Assert.Contains("customapp", customDir, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("models", customDir);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveModelsDirectory_WhenNullOrWhitespace_ReturnsDefaultModelsDirectory(string? configuredDir)
    {
        var resolved = LocalModelPathResolver.ResolveModelsDirectory(configuredDir);
        var expected = LocalModelPathResolver.GetDefaultModelsDirectory();

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void ResolveModelsDirectory_WhenConfiguredDirectoryProvided_ReturnsFullPath()
    {
        var customPath = Path.Combine(Path.GetTempPath(), "custom_llama_models");

        var resolved = LocalModelPathResolver.ResolveModelsDirectory(customPath);

        Assert.Equal(Path.GetFullPath(customPath), resolved);
    }

    [Fact]
    public void ResolveModelsDirectory_WhenRelativeDirectoryProvided_ResolvesToFullPath()
    {
        var relativePath = "sub_models_folder";

        var resolved = LocalModelPathResolver.ResolveModelsDirectory(relativePath);

        Assert.True(Path.IsPathRooted(resolved));
        Assert.Equal(Path.GetFullPath(relativePath), resolved);
    }
}

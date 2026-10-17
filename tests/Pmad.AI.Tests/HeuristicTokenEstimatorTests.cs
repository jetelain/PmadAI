namespace Pmad.AI.Tests;

public class HeuristicTokenEstimatorTests
{
    [Fact]
    public void EstimateTokens_NullOrEmptyList_ReturnsZero()
    {
        Assert.Equal(0, HeuristicTokenEstimator.EstimateTokens((IReadOnlyList<string>)null!));
        Assert.Equal(0, HeuristicTokenEstimator.EstimateTokens(new List<string>()));
    }

    [Fact]
    public void EstimateTokens_ListWithEmptyOrNullStrings_ReturnsZero()
    {
        Assert.Equal(0, HeuristicTokenEstimator.EstimateTokens(new[] { "", "   ", "" }));
    }

    [Fact]
    public void EstimateTokens_SingleWord_ReturnsAtLeastOne()
    {
        var count = HeuristicTokenEstimator.EstimateTokens(["Hello"]);
        Assert.True(count >= 1);
    }

    [Fact]
    public void EstimateTokens_MultipleSegments_SumsTokensAcrossSegments()
    {
        var part1 = "Hello, how are you?";
        var part2 = "I am an AI assistant.";

        var individual1 = HeuristicTokenEstimator.EstimateTokens([part1]);
        var individual2 = HeuristicTokenEstimator.EstimateTokens([part2]);
        var combined = HeuristicTokenEstimator.EstimateTokens([part1, part2]);

        Assert.Equal(individual1 + individual2, combined);
    }

    [Fact]
    public void EstimateTokens_ExtensionMethod_WorksWithSingleString()
    {
        var count = HeuristicTokenEstimator.EstimateTokens("This is a single prompt string.");
        Assert.True(count >= 5);
    }

    [Fact]
    public void EstimateTokens_CodeAndJson_EstimatesPunctuationAndSymbols()
    {
        var code = "function add(a, b) { return a + b; }";
        var count = HeuristicTokenEstimator.EstimateTokens([code]);

        // Code has multiple symbols and keywords, should estimate appropriately
        Assert.True(count >= 8);
    }

    [Fact]
    public void EstimateTokens_NonAsciiCharacters_CountsUnicode()
    {
        var french = "L'intelligence artificielle générative révolutionne le monde.";
        var count = HeuristicTokenEstimator.EstimateTokens([french]);

        Assert.True(count >= 8);
    }
}


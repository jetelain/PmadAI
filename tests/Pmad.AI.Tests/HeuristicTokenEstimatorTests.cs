namespace Pmad.AI.Tests;

public class HeuristicTokenEstimatorTests
{
    [Fact]
    public void Default_HasExpectedStandardFactors()
    {
        var defaultEstimator = HeuristicTokenEstimator.Default;
        Assert.NotNull(defaultEstimator);
        Assert.Equal(1.25, defaultEstimator.WordFactor);
        Assert.Equal(0.8, defaultEstimator.SymbolFactor);
        Assert.Equal(1.0, defaultEstimator.NonAsciiFactor);
        Assert.Equal(3.8, defaultEstimator.CharFloorFactor);
    }

    [Fact]
    public void Constructor_CustomFactors_AssignedCorrectly()
    {
        var estimator = new HeuristicTokenEstimator(
            wordFactor: 2.0,
            symbolFactor: 1.5,
            nonAsciiFactor: 2.5,
            charFloorFactor: 5.0);

        Assert.Equal(2.0, estimator.WordFactor);
        Assert.Equal(1.5, estimator.SymbolFactor);
        Assert.Equal(2.5, estimator.NonAsciiFactor);
        Assert.Equal(5.0, estimator.CharFloorFactor);
    }

    [Fact]
    public void ObjectInitializer_CustomFactors_AssignedCorrectly()
    {
        var estimator = new HeuristicTokenEstimator
        {
            WordFactor = 1.75,
            SymbolFactor = 1.2,
            NonAsciiFactor = 1.8,
            CharFloorFactor = 4.2
        };

        Assert.Equal(1.75, estimator.WordFactor);
        Assert.Equal(1.2, estimator.SymbolFactor);
        Assert.Equal(1.8, estimator.NonAsciiFactor);
        Assert.Equal(4.2, estimator.CharFloorFactor);
    }

    [Fact]
    public void CustomFactors_AffectTokenCalculation()
    {
        var defaultEstimator = HeuristicTokenEstimator.Default;
        var highWordEstimator = new HeuristicTokenEstimator(wordFactor: 5.0, charFloorFactor: 0);
        var highSymbolEstimator = new HeuristicTokenEstimator(symbolFactor: 5.0, charFloorFactor: 0);
        var highUnicodeEstimator = new HeuristicTokenEstimator(nonAsciiFactor: 5.0, charFloorFactor: 0);

        var wordsOnly = "one two three four five";
        var symbolsOnly = "!@#$%^&*()_+{}|:<>?";
        var unicodeOnly = "日本語のテスト文字列";

        Assert.True(highWordEstimator.Estimate(wordsOnly) > defaultEstimator.Estimate(wordsOnly));
        Assert.True(highSymbolEstimator.Estimate(symbolsOnly) > defaultEstimator.Estimate(symbolsOnly));
        Assert.True(highUnicodeEstimator.Estimate(unicodeOnly) > defaultEstimator.Estimate(unicodeOnly));
    }

    [Fact]
    public void CustomCharFloor_EnforcesOrDisablesFloor()
    {
        // Long text with very few words: floor dominates
        var longWord = new string('a', 100);

        var floorEstimator = new HeuristicTokenEstimator(wordFactor: 1.0, charFloorFactor: 2.0); // 100 / 2 = 50
        var disabledFloorEstimator = new HeuristicTokenEstimator(wordFactor: 1.0, charFloorFactor: 0); // 1 word * 1 = 1

        Assert.Equal(50, floorEstimator.Estimate(longWord));
        Assert.Equal(1, disabledFloorEstimator.Estimate(longWord));
    }

    [Fact]
    public void Estimate_NullOrEmptyList_ReturnsZero()
    {
        Assert.Equal(0, HeuristicTokenEstimator.Default.Estimate((IReadOnlyList<string>)null!));
        Assert.Equal(0, HeuristicTokenEstimator.Default.Estimate(new List<string>()));
    }

    [Fact]
    public void Estimate_ListWithEmptyOrNullStrings_ReturnsZero()
    {
        Assert.Equal(0, HeuristicTokenEstimator.Default.Estimate(new[] { "", "   ", "" }));
    }

    [Fact]
    public void Estimate_SingleWord_ReturnsAtLeastOne()
    {
        var count = HeuristicTokenEstimator.Default.Estimate(["Hello"]);
        Assert.True(count >= 1);
    }

    [Fact]
    public void Estimate_MultipleSegments_SumsTokensAcrossSegments()
    {
        var part1 = "Hello, how are you?";
        var part2 = "I am an AI assistant.";

        var individual1 = HeuristicTokenEstimator.Default.Estimate([part1]);
        var individual2 = HeuristicTokenEstimator.Default.Estimate([part2]);
        var combined = HeuristicTokenEstimator.Default.Estimate([part1, part2]);

        Assert.Equal(individual1 + individual2, combined);
    }

    [Fact]
    public void Estimate_WorksWithSingleString()
    {
        var count = HeuristicTokenEstimator.Default.Estimate("This is a single prompt string.");
        Assert.True(count >= 5);
    }

    [Fact]
    public void Estimate_CodeAndJson_EstimatesPunctuationAndSymbols()
    {
        var code = "function add(a, b) { return a + b; }";
        var count = HeuristicTokenEstimator.Default.Estimate([code]);

        // Code has multiple symbols and keywords, should estimate appropriately
        Assert.True(count >= 8);
    }

    [Fact]
    public void Estimate_NonAsciiCharacters_CountsUnicode()
    {
        var french = "L'intelligence artificielle générative révolutionne le monde.";
        var count = HeuristicTokenEstimator.Default.Estimate([french]);

        Assert.True(count >= 8);
    }
}

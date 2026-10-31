namespace Pmad.AI.Tokens;

/// <summary>
/// A fast, zero-allocation token estimator that approximates token counts using character,
/// word, punctuation, and Unicode heuristics without loading any vocabulary tables into memory.
/// Keeps memory footprint at 0 MB heap and executes ~100x faster than full BPE tokenization.
/// Factors can be customized per instance, with standard defaults accessible via <see cref="Default"/>.
/// </summary>
public class HeuristicTokenEstimator
{
    /// <summary>
    /// Default factor applied to word count when estimating tokens (1.25).
    /// </summary>
    public const double DefaultWordFactor = 1.25;

    /// <summary>
    /// Default factor applied to punctuation and symbols count when estimating tokens (0.8).
    /// </summary>
    public const double DefaultSymbolFactor = 0.8;

    /// <summary>
    /// Default factor applied to non-ASCII / Unicode character count when estimating tokens (1.0).
    /// </summary>
    public const double DefaultNonAsciiFactor = 1.0;

    /// <summary>
    /// Default characters-per-token floor factor (3.8 chars/token).
    /// </summary>
    public const double DefaultCharFloorFactor = 3.8;

    /// <summary>
    /// Gets the shared default instance configured with standard heuristic factors.
    /// </summary>
    public static HeuristicTokenEstimator Default { get; } = new();

    /// <summary>
    /// Gets a preset fine-tuned for the Qwen model family (Qwen 3.5, Qwen 3.6, Qwen 3.8)
    /// based on empirical tokenizer calibration (WordFactor=1.17, SymbolFactor=0.88, NonAsciiFactor=0.40, CharFloorFactor=3.5).
    /// </summary>
    public static HeuristicTokenEstimator Qwen { get; } = new(
        wordFactor: 1.17,
        symbolFactor: 0.88,
        nonAsciiFactor: 0.40,
        charFloorFactor: 3.5);

    /// <summary>
    /// Gets a preset fine-tuned for Google Gemma 4 models (E2B, E4B, 12B, 26B, 31B)
    /// based on empirical tokenizer calibration (WordFactor=1.43, SymbolFactor=0.72, NonAsciiFactor=0.37, CharFloorFactor=3.6).
    /// </summary>
    public static HeuristicTokenEstimator Gemma { get; } = new(
        wordFactor: 1.43,
        symbolFactor: 0.72,
        nonAsciiFactor: 0.37,
        charFloorFactor: 3.6);

    /// <summary>
    /// Gets the factor applied to word count.
    /// </summary>
    public double WordFactor { get; init; } = DefaultWordFactor;

    /// <summary>
    /// Gets the factor applied to punctuation and symbol count.
    /// </summary>
    public double SymbolFactor { get; init; } = DefaultSymbolFactor;

    /// <summary>
    /// Gets the factor applied to non-ASCII character count.
    /// </summary>
    public double NonAsciiFactor { get; init; } = DefaultNonAsciiFactor;

    /// <summary>
    /// Gets the character floor divisor (characters per token).
    /// When positive, enforces a minimum token count of <c>ceil(text.Length / CharFloorFactor)</c>.
    /// </summary>
    public double CharFloorFactor { get; init; } = DefaultCharFloorFactor;

    /// <summary>
    /// Gets the safety margin multiplier fraction applied to the final estimate (e.g. 0.05 for +5% headroom).
    /// Defaults to 0.0 (no margin).
    /// </summary>
    public double SafetyMargin { get; init; } = 0.0;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeuristicTokenEstimator"/> class with default factors.
    /// </summary>
    public HeuristicTokenEstimator()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HeuristicTokenEstimator"/> class with customizable factors.
    /// </summary>
    /// <param name="wordFactor">Factor applied to word count (default: 1.25).</param>
    /// <param name="symbolFactor">Factor applied to punctuation and symbols (default: 0.8).</param>
    /// <param name="nonAsciiFactor">Factor applied to non-ASCII / Unicode characters (default: 1.0).</param>
    /// <param name="charFloorFactor">Characters per token floor divisor (default: 3.8). When &lt;= 0, character floor is disabled.</param>
    /// <param name="safetyMargin">Safety margin fraction applied to the final estimate (default: 0.0).</param>
    public HeuristicTokenEstimator(
        double wordFactor = DefaultWordFactor,
        double symbolFactor = DefaultSymbolFactor,
        double nonAsciiFactor = DefaultNonAsciiFactor,
        double charFloorFactor = DefaultCharFloorFactor,
        double safetyMargin = 0.0)
    {
        WordFactor = wordFactor;
        SymbolFactor = symbolFactor;
        NonAsciiFactor = nonAsciiFactor;
        CharFloorFactor = charFloorFactor;
        SafetyMargin = safetyMargin;
    }

    /// <summary>
    /// Analyzes the character span to extract structural token metrics (words, symbols, non-ASCII characters, character count)
    /// without any heap allocation.
    /// </summary>
    /// <param name="text">The character span to analyze.</param>
    /// <returns>A <see cref="TokenMetrics"/> value containing the measured counts.</returns>
    public static TokenMetrics Analyze(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty || text.IsWhiteSpace())
        {
            return default;
        }

        int words = 0;
        int symbols = 0;
        int nonAscii = 0;
        bool inWord = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c > 127)
            {
                nonAscii++;
                inWord = false;
            }
            else if (char.IsLetterOrDigit(c))
            {
                if (!inWord)
                {
                    words++;
                    inWord = true;
                }
            }
            else
            {
                inWord = false;
                if (!char.IsWhiteSpace(c))
                {
                    symbols++;
                }
            }
        }

        return new TokenMetrics(words, symbols, nonAscii, text.Length);
    }

    /// <summary>
    /// Analyzes a collection of text strings to aggregate their structural token metrics.
    /// </summary>
    /// <param name="texts">The collection of text strings to analyze.</param>
    /// <returns>A <see cref="TokenMetrics"/> value containing the aggregated counts.</returns>
    public static TokenMetrics Analyze(IReadOnlyList<string>? texts)
    {
        if (texts == null || texts.Count == 0)
        {
            return default;
        }

        int words = 0;
        int symbols = 0;
        int nonAscii = 0;
        int charCount = 0;

        for (int i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            if (!string.IsNullOrEmpty(text))
            {
                var m = Analyze(text.AsSpan());
                words += m.Words;
                symbols += m.Symbols;
                nonAscii += m.NonAscii;
                charCount += m.CharCount;
            }
        }

        return new TokenMetrics(words, symbols, nonAscii, charCount);
    }

    /// <summary>
    /// Estimates the token count from pre-computed structural metrics.
    /// </summary>
    /// <param name="metrics">The collected token metrics.</param>
    /// <returns>The estimated token count.</returns>
    public int Estimate(in TokenMetrics metrics)
    {
        if (metrics.Words == 0 && metrics.Symbols == 0 && metrics.NonAscii == 0)
        {
            return 0;
        }

        int tokenEstimate = (int)Math.Ceiling(metrics.Words * WordFactor + metrics.Symbols * SymbolFactor + metrics.NonAscii * NonAsciiFactor);
        int charFloor = CharFloorFactor > 0 ? (int)Math.Ceiling(metrics.CharCount / CharFloorFactor) : 0;

        int finalEstimate = Math.Max(1, Math.Max(tokenEstimate, charFloor));
        if (SafetyMargin > 0)
        {
            finalEstimate = (int)Math.Ceiling(finalEstimate * (1.0 + SafetyMargin));
        }

        return finalEstimate;
    }

    /// <summary>
    /// Estimates the total token count across a list of texts.
    /// </summary>
    /// <param name="texts">The collection of text strings to estimate.</param>
    /// <returns>The estimated total token count.</returns>
    public int Estimate(IReadOnlyList<string>? texts)
    {
        if (texts == null || texts.Count == 0)
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            if (!string.IsNullOrEmpty(text))
            {
                total += Estimate(text.AsSpan());
            }
        }

        return total;
    }

    /// <summary>
    /// Estimates the token count for the specified character span.
    /// </summary>
    /// <param name="text">The character span to estimate.</param>
    /// <returns>The estimated token count.</returns>
    public int Estimate(ReadOnlySpan<char> text)
    {
        var metrics = Analyze(text);
        return Estimate(in metrics);
    }
}

/// <summary>
/// Represents the four primitive structural metrics measured by <see cref="HeuristicTokenEstimator"/>
/// when profiling or estimating text tokens.
/// </summary>
/// <param name="Words">Count of ASCII alphanumeric words.</param>
/// <param name="Symbols">Count of ASCII punctuation, symbol, and formatting characters.</param>
/// <param name="NonAscii">Count of non-ASCII / Unicode code points.</param>
/// <param name="CharCount">Total character length of the analyzed text.</param>
public readonly record struct TokenMetrics(int Words, int Symbols, int NonAscii, int CharCount)
{
    /// <summary>
    /// Gets whether all metric counts are zero.
    /// </summary>
    public bool IsEmpty => Words == 0 && Symbols == 0 && NonAscii == 0 && CharCount == 0;

    /// <summary>
    /// Combines two sets of token metrics by summing their individual components.
    /// </summary>
    public static TokenMetrics operator +(in TokenMetrics left, in TokenMetrics right) =>
        new(left.Words + right.Words, left.Symbols + right.Symbols, left.NonAscii + right.NonAscii, left.CharCount + right.CharCount);
}

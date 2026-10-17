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
    public HeuristicTokenEstimator(
        double wordFactor = DefaultWordFactor,
        double symbolFactor = DefaultSymbolFactor,
        double nonAsciiFactor = DefaultNonAsciiFactor,
        double charFloorFactor = DefaultCharFloorFactor)
    {
        WordFactor = wordFactor;
        SymbolFactor = symbolFactor;
        NonAsciiFactor = nonAsciiFactor;
        CharFloorFactor = charFloorFactor;
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
        if (text.IsEmpty || text.IsWhiteSpace())
        {
            return 0;
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

        if (words == 0 && symbols == 0 && nonAscii == 0)
        {
            return 0;
        }

        // Blended heuristic: words (~1.25 tokens/word), punctuation/symbols (0.8 tokens), and non-ASCII (1 token).
        // Fall back to a standard ~3.8 chars/token floor for atypical dense formatting.
        int tokenEstimate = (int)Math.Ceiling(words * WordFactor + symbols * SymbolFactor + nonAscii * NonAsciiFactor);
        int charFloor = CharFloorFactor > 0 ? (int)Math.Ceiling(text.Length / CharFloorFactor) : 0;

        return Math.Max(1, Math.Max(tokenEstimate, charFloor));
    }
}

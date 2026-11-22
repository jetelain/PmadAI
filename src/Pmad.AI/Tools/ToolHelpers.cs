using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Pmad.AI.Tools;

/// <summary>
/// Helper methods for tool name resolution, identifier normalization, and lenient tool dispatch.
/// Pure, reflection-free, and Native AOT compatible.
/// </summary>
public static class ToolHelpers
{
    private const double LongMinAsDouble = -9223372036854775808.0; // -2^63, exact in IEEE-754 double
    private const double LongMaxExclusiveAsDouble = 9223372036854775808.0; // +2^63, exact in IEEE-754 double ((double)long.MaxValue rounds up to this)

    /// <summary>
    /// Normalizes a tool identifier by stripping separators (<c>_</c> and <c>-</c>) and converting
    /// to lowercase invariant for tolerant matching during tool dispatch.
    /// </summary>
    public static string NormalizeToolIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c != '_' && c != '-')
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Attempts to coerce an integral numeric value or string (including integral floating-point
    /// values like <c>"240.0"</c> or <c>240.0</c>) into a boxed <see cref="int"/> (when within
    /// <see cref="int"/> range) or <see cref="long"/>, preserving existing integral CLR types.
    /// When <paramref name="allowRounding"/> is <c>true</c>, finite fractional values are rounded
    /// to the nearest integer (<see cref="MidpointRounding.AwayFromZero"/>).
    /// </summary>
    internal static bool TryCoerceIntegralValue(
        object? value,
        bool allowHashPrefix,
        [NotNullWhen(true)] out object? boxedIntOrLong,
        bool allowRounding = false)
    {
        if (value == null)
        {
            boxedIntOrLong = null;
            return false;
        }

        if (value is int or long or short or byte)
        {
            boxedIntOrLong = value;
            return true;
        }

        if (TryCoerceToInt64(value, allowHashPrefix, out var longVal, allowRounding))
        {
            boxedIntOrLong = longVal >= int.MinValue && longVal <= int.MaxValue
                ? (object)(int)longVal
                : longVal;
            return true;
        }

        boxedIntOrLong = null;
        return false;
    }

    /// <summary>
    /// Attempts to coerce an integral numeric value or string (including integral floating-point
    /// values and optional scientific notation such as <c>"1e2"</c>) to an exact <see cref="long"/>.
    /// Rejects non-finite and out-of-range values, and rejects fractional values unless
    /// <paramref name="allowRounding"/> is <c>true</c>.
    /// </summary>
    internal static bool TryCoerceToInt64(object? value, bool allowHashPrefix, out long result, bool allowRounding = false)
    {
        if (value == null)
        {
            result = 0;
            return false;
        }

        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number)
            {
                if (je.TryGetInt64(out result))
                {
                    return true;
                }

                return TryParseIntegralString(je.GetRawText(), allowHashPrefix: false, out result, allowRounding);
            }

            if (je.ValueKind == JsonValueKind.String)
            {
                return TryParseIntegralString(je.GetString(), allowHashPrefix, out result, allowRounding);
            }

            result = 0;
            return false;
        }

        switch (value)
        {
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case short s:
                result = s;
                return true;
            case byte b:
                result = b;
                return true;
            case sbyte sb:
                result = sb;
                return true;
            case ushort us:
                result = us;
                return true;
            case uint ui:
                result = ui;
                return true;
            case ulong ul:
                if (ul <= long.MaxValue)
                {
                    result = (long)ul;
                    return true;
                }
                result = 0;
                return false;
            case decimal dec:
                return TryCoerceDecimalToInt64(dec, out result, allowRounding);
            case float f:
                return TryCoerceDoubleToInt64(f, out result, allowRounding);
            case double d:
                return TryCoerceDoubleToInt64(d, out result, allowRounding);
            case string str:
                return TryParseIntegralString(str, allowHashPrefix, out result, allowRounding);
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryParseIntegralString(string? str, bool allowHashPrefix, out long result, bool allowRounding)
    {
        if (string.IsNullOrWhiteSpace(str))
        {
            result = 0;
            return false;
        }

        var candidate = str.Trim();
        if (allowHashPrefix && candidate.StartsWith('#'))
        {
            candidate = candidate[1..].Trim();
            if (candidate.Length == 0)
            {
                result = 0;
                return false;
            }
        }

        if (long.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        if (decimal.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out var decVal))
        {
            // Prevent decimal.TryParse silent underflow/truncation (e.g. "1e-30" -> 0m) from masking fractional strings.
            if (!allowRounding && !IsExactIntegralDecimalString(candidate))
            {
                result = 0;
                return false;
            }

            return TryCoerceDecimalToInt64(decVal, out result, allowRounding);
        }

        result = 0;
        return false;
    }

    private static bool TryCoerceDecimalToInt64(decimal dec, out long result, bool allowRounding)
    {
        var integralDec = allowRounding ? Math.Round(dec, 0, MidpointRounding.AwayFromZero) : dec;
        if (decimal.Truncate(integralDec) == integralDec &&
            integralDec >= long.MinValue &&
            integralDec <= long.MaxValue)
        {
            result = (long)integralDec;
            return true;
        }

        result = 0;
        return false;
    }

    private static bool IsExactIntegralDecimalString(ReadOnlySpan<char> s)
    {
        if (s.Length > 0 && (s[0] == '+' || s[0] == '-'))
        {
            s = s[1..];
        }

        int expIdx = s.IndexOfAny('e', 'E');
        ReadOnlySpan<char> mantissa = expIdx >= 0 ? s[..expIdx] : s;
        ReadOnlySpan<char> expSpan = expIdx >= 0 ? s[(expIdx + 1)..] : default;

        bool hasDigit = false;
        bool hasNonZeroDigit = false;
        bool seenDot = false;
        int fracDigits = 0;
        int trailingZeros = 0;

        foreach (char c in mantissa)
        {
            if (c == '.')
            {
                if (seenDot)
                {
                    return false;
                }
                seenDot = true;
            }
            else if (c == '0')
            {
                hasDigit = true;
                if (seenDot)
                {
                    fracDigits++;
                }
                if (hasNonZeroDigit)
                {
                    trailingZeros++;
                }
            }
            else if (c is >= '1' and <= '9')
            {
                hasDigit = true;
                hasNonZeroDigit = true;
                if (seenDot)
                {
                    fracDigits++;
                }
                trailingZeros = 0;
            }
            else
            {
                return false;
            }
        }

        if (!hasDigit)
        {
            return false;
        }

        if (!hasNonZeroDigit)
        {
            return true;
        }

        long exponent = 0;
        if (expIdx >= 0)
        {
            if (expSpan.IsEmpty || !long.TryParse(expSpan, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
            {
                return false;
            }
        }

        return exponent >= (long)fracDigits - trailingZeros;
    }

    private static bool TryCoerceDoubleToInt64(double d, out long result, bool allowRounding)
    {
        if (!double.IsNaN(d) && !double.IsInfinity(d))
        {
            var integral = allowRounding ? Math.Round(d, 0, MidpointRounding.AwayFromZero) : d;
            if (Math.Truncate(integral) == integral &&
                integral >= LongMinAsDouble &&
                integral < LongMaxExclusiveAsDouble)
            {
                result = (long)integral;
                return true;
            }
        }

        result = 0;
        return false;
    }
}

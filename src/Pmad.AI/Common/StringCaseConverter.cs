using System.Text;

namespace Pmad.AI.Common;

/// <summary>
/// Identifier case-conversion helpers used to normalize parameter, property, and enum spellings
/// (snake_case, camelCase, PascalCase) during lenient tool dispatch. Pure, allocation-light,
/// reflection-free and Native AOT compatible.
/// </summary>
public static class StringCaseConverter
{
    /// <summary>
    /// Converts a name to snake_case, treating uppercase boundaries and existing separators
    /// (<c>_</c>, <c>-</c>, whitespace) as word separators.
    /// </summary>
    public static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c is '_' or '-' || char.IsWhiteSpace(c))
            {
                if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                {
                    sb.Append('_');
                }
            }
            else if (char.IsUpper(c))
            {
                if (i > 0 && name[i - 1] != '_' && name[i - 1] != '-' && !char.IsWhiteSpace(name[i - 1]) && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                    {
                        sb.Append('_');
                    }
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Converts a name to camelCase, treating existing separators (<c>_</c>, <c>-</c>, whitespace)
    /// as word boundaries; the first word is lower-cased and subsequent words are capitalized.
    /// </summary>
    public static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new StringBuilder(name.Length);
        bool capitalizeNext = false;
        bool isFirst = true;
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c is '_' or '-' || char.IsWhiteSpace(c))
            {
                capitalizeNext = true;
            }
            else
            {
                if (isFirst)
                {
                    sb.Append(char.ToLowerInvariant(c));
                    isFirst = false;
                    capitalizeNext = false;
                }
                else if (capitalizeNext)
                {
                    sb.Append(char.ToUpperInvariant(c));
                    capitalizeNext = false;
                }
                else
                {
                    sb.Append(c);
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Converts a name to PascalCase, treating existing separators (<c>_</c>, <c>-</c>, whitespace)
    /// as word boundaries; every word is capitalized.
    /// </summary>
    public static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new StringBuilder(name.Length);
        bool capitalizeNext = true;
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c is '_' or '-' || char.IsWhiteSpace(c))
            {
                capitalizeNext = true;
            }
            else
            {
                if (capitalizeNext)
                {
                    sb.Append(char.ToUpperInvariant(c));
                    capitalizeNext = false;
                }
                else
                {
                    sb.Append(c);
                }
            }
        }
        return sb.ToString();
    }
}

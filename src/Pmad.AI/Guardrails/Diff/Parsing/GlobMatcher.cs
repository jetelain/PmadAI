using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Pmad.AI.Guardrails.Diff;

internal static class GlobMatcher
{
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsMatch(string path, string pattern)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        path = path.Replace('\\', '/').Trim('/');
        pattern = pattern.Replace('\\', '/').Trim('/');

        if (string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var regex = RegexCache.GetOrAdd(pattern, static p => CreateRegex(p));
        return regex.IsMatch(path);
    }

    private static Regex CreateRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        bool hasSlash = pattern.Contains('/');

        if (!hasSlash)
        {
            // Pattern like "*.txt" should match in root or any subfolder
            sb.Append("(?:.*/)?");
        }

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                // "**"
                i++;
                if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                {
                    // "/**/" or "dir/**/"
                    i++;
                    sb.Append("(?:.*/)?");
                }
                else
                {
                    // "**" at the end of pattern or trailing
                    sb.Append(".*");
                }
            }
            else if (c == '*')
            {
                sb.Append("[^/]*");
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else if (c is '.' or '$' or '^' or '{' or '}' or '(' or ')' or '+' or '|' or '[' or ']')
            {
                sb.Append('\\').Append(c);
            }
            else
            {
                sb.Append(c);
            }
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    }
}


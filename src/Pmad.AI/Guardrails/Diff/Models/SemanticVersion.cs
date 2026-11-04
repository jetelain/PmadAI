namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Lightweight semantic version struct supporting SemVer 2.0.0 parsing, 4-part versions, and precedence comparison.
/// </summary>
public readonly record struct SemanticVersion(
    int Major,
    int Minor,
    int Patch,
    int Revision,
    string? Prerelease,
    string? BuildMetadata) : IComparable<SemanticVersion>
{
    public SemanticVersion(int major, int minor, int patch, string? prerelease = null, string? buildMetadata = null)
        : this(major, minor, patch, 0, prerelease, buildMetadata)
    {
    }

    public int CompareTo(SemanticVersion other)
    {
        int cmp = Major.CompareTo(other.Major);
        if (cmp != 0) return cmp;

        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0) return cmp;

        cmp = Patch.CompareTo(other.Patch);
        if (cmp != 0) return cmp;

        cmp = Revision.CompareTo(other.Revision);
        if (cmp != 0) return cmp;

        // A normal release has higher precedence than a pre-release
        if (Prerelease == null && other.Prerelease == null) return 0;
        if (Prerelease == null && other.Prerelease != null) return 1;
        if (Prerelease != null && other.Prerelease == null) return -1;

        return ComparePrerelease(Prerelease!, other.Prerelease!);
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Attempts to parse a version string into a <see cref="SemanticVersion"/>, stripping leading range operators (^, ~, &gt;=, ==, v).
    /// </summary>
    public static bool TryParse(string? input, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        ReadOnlySpan<char> span = input.AsSpan().Trim();

        // Strip leading range symbols, brackets, and whitespace: ^, ~, >=, <=, ==, =, >, <, v, V, [, (
        while (span.Length > 0 && (span[0] is '^' or '~' or '>' or '<' or '=' or '!' or 'v' or 'V' or '[' or '(' || char.IsWhiteSpace(span[0])))
        {
            span = span[1..].TrimStart();
        }

        // If there's a comma (range such as ">= 1.2.0, < 2.0.0" or "[1.2.0, 2.0.0)"), take the first part
        int commaIndex = span.IndexOf(',');
        if (commaIndex >= 0)
        {
            span = span[..commaIndex].Trim();
        }

        // Strip trailing range brackets: ], )
        while (span.Length > 0 && (span[^1] is ']' or ')' || char.IsWhiteSpace(span[^1])))
        {
            span = span[..^1];
        }

        if (span.IsEmpty)
        {
            return false;
        }

        string? build = null;
        int plusIndex = span.IndexOf('+');
        if (plusIndex >= 0)
        {
            build = span[(plusIndex + 1)..].ToString();
            span = span[..plusIndex];
        }

        string? prerelease = null;
        int dashIndex = span.IndexOf('-');
        if (dashIndex >= 0)
        {
            prerelease = span[(dashIndex + 1)..].ToString();
            span = span[..dashIndex];
        }

        // Parse major.minor.patch[.revision]
        int dot1 = span.IndexOf('.');
        if (dot1 < 0)
        {
            if (int.TryParse(span, out int majorOnly))
            {
                version = new SemanticVersion(majorOnly, 0, 0, 0, prerelease, build);
                return true;
            }
            return false;
        }

        if (!int.TryParse(span[..dot1], out int major))
        {
            return false;
        }

        ReadOnlySpan<char> rest = span[(dot1 + 1)..];
        int dot2 = rest.IndexOf('.');
        if (dot2 < 0)
        {
            if (int.TryParse(rest, out int minorOnly))
            {
                version = new SemanticVersion(major, minorOnly, 0, 0, prerelease, build);
                return true;
            }
            return false;
        }

        if (!int.TryParse(rest[..dot2], out int minor))
        {
            return false;
        }

        ReadOnlySpan<char> patchSpan = rest[(dot2 + 1)..];
        int dot3 = patchSpan.IndexOf('.');
        int patch;
        int revision = 0;

        if (dot3 >= 0)
        {
            if (!int.TryParse(patchSpan[..dot3], out patch))
            {
                return false;
            }

            ReadOnlySpan<char> revSpan = patchSpan[(dot3 + 1)..];
            int dot4 = revSpan.IndexOf('.');
            if (dot4 >= 0)
            {
                if (!int.TryParse(revSpan[..dot4], out revision))
                {
                    return false;
                }
            }
            else
            {
                if (!int.TryParse(revSpan, out revision))
                {
                    return false;
                }
            }
        }
        else
        {
            int nonDigit = 0;
            while (nonDigit < patchSpan.Length && char.IsDigit(patchSpan[nonDigit]))
            {
                nonDigit++;
            }

            if (nonDigit == patchSpan.Length)
            {
                if (!int.TryParse(patchSpan, out patch))
                {
                    return false;
                }
            }
            else if (nonDigit > 0)
            {
                if (!int.TryParse(patchSpan[..nonDigit], out patch))
                {
                    return false;
                }
                string extraPre = patchSpan[nonDigit..].ToString();
                prerelease = prerelease != null ? $"{extraPre}.{prerelease}" : extraPre;
            }
            else
            {
                return false;
            }
        }

        version = new SemanticVersion(major, minor, patch, revision, prerelease, build);
        return true;
    }

    private static int ComparePrerelease(string a, string b)
    {
        var segsA = a.Split('.');
        var segsB = b.Split('.');
        int minLen = Math.Min(segsA.Length, segsB.Length);

        for (int i = 0; i < minLen; i++)
        {
            string sA = segsA[i];
            string sB = segsB[i];

            bool isNumA = int.TryParse(sA, out int numA);
            bool isNumB = int.TryParse(sB, out int numB);

            if (isNumA && isNumB)
            {
                int cmp = numA.CompareTo(numB);
                if (cmp != 0) return cmp;
            }
            else if (isNumA)
            {
                // Numeric identifiers always have lower precedence than non-numeric
                return -1;
            }
            else if (isNumB)
            {
                return 1;
            }
            else
            {
                int cmp = string.CompareOrdinal(sA, sB);
                if (cmp != 0) return cmp;
            }
        }

        return segsA.Length.CompareTo(segsB.Length);
    }

    public override string ToString()
    {
        string v = Revision > 0 ? $"{Major}.{Minor}.{Patch}.{Revision}" : $"{Major}.{Minor}.{Patch}";
        if (!string.IsNullOrEmpty(Prerelease))
        {
            v += $"-{Prerelease}";
        }
        if (!string.IsNullOrEmpty(BuildMetadata))
        {
            v += $"+{BuildMetadata}";
        }
        return v;
    }
}


using System.Text;

namespace Pmad.AI.Tools;

/// <summary>
/// Helper methods for tool name resolution, identifier normalization, and lenient tool dispatch.
/// Pure, reflection-free, and Native AOT compatible.
/// </summary>
public static class ToolHelpers
{
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
}

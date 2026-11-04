namespace Pmad.AI.DiffGuard.Cli.Formatting;

public static class ReportPathHelper
{
    public static string NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Replace('\\', '/').TrimStart('/');
    }
}


using System.Runtime.InteropServices;

namespace Pmad.AI.LlamaSharp;

/// <summary>
/// Resolves storage paths for local model files across operating systems.
/// </summary>
public static class LocalModelPathResolver
{
    /// <summary>
    /// Returns the default user-scoped models directory:
    /// `%LOCALAPPDATA%\{appName}\models\` on Windows,
    /// or `~/.{appName}/models/` on Linux/macOS.
    /// </summary>
    public static string GetDefaultModelsDirectory(string appName = "pmad")
    {
        var effectiveAppName = string.IsNullOrWhiteSpace(appName) ? "pmad" : appName.Trim();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var homeModels = Path.Combine(home, $".{effectiveAppName.ToLowerInvariant()}", "models");
        if (Directory.Exists(homeModels))
        {
            return homeModels;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }
            return Path.Combine(localAppData, effectiveAppName, "models");
        }

        var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgData))
        {
            return Path.Combine(xdgData.Trim(), effectiveAppName.ToLowerInvariant(), "models");
        }

        return homeModels;
    }

    /// <summary>
    /// Resolves the absolute directory for a given models directory,
    /// falling back to the default models cache directory when null or empty.
    /// </summary>
    public static string ResolveModelsDirectory(string? configuredDirectory, string appName = "pmad")
    {
        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return GetDefaultModelsDirectory(appName);
        }

        return Path.GetFullPath(configuredDirectory.Trim());
    }
}
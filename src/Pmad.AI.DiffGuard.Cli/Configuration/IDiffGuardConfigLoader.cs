namespace Pmad.AI.DiffGuard.Cli.Configuration;

public interface IDiffGuardConfigLoader
{
    DiffGuardPolicy LoadPolicy(string? repoRoot = null, string? customRepoConfigPath = null);
}


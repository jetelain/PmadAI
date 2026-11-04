using System.CommandLine;
using Pmad.AI.DiffGuard.Cli.Cli;

namespace Pmad.AI.DiffGuard.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = CommandLineConfiguration.CreateRootCommand();
        return await rootCommand.Parse(args).InvokeAsync();
    }
}


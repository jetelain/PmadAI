using Pmad.AI.DiffGuard.Cli.Execution;

namespace Pmad.AI.DiffGuard.Cli.Formatting;

public interface IReportFormatter
{
    Task FormatAsync(DiffGuardReportModel report, TextWriter writer, CancellationToken cancellationToken = default);
}


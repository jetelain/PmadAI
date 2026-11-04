using Microsoft.Extensions.AI;
using Pmad.AI.DiffGuard.Cli.Cli;

namespace Pmad.AI.DiffGuard.Cli.Llm;

public interface ILlmJudgeClientFactory
{
    (IChatClient? Client, IDisposable? Disposable) CreateClient(DiffGuardCliOptions options);
}


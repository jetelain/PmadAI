using Microsoft.Extensions.AI;

namespace Pmad.AI.Tools;

/// <summary>
/// Decorates an <see cref="AIFunction"/> with automatic argument normalization
/// using a <see cref="LenientToolDispatcher"/> before invocation.
/// </summary>
public sealed class LenientAIFunction : DelegatingAIFunction
{
    private readonly LenientToolDispatcher _dispatcher;

    public LenientAIFunction(AIFunction innerFunction, LenientToolDispatcher dispatcher)
        : base(innerFunction)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        if (!_dispatcher.TryGetRegisteredTool(innerFunction.Name, out _))
        {
            _dispatcher.RegisterTool(innerFunction);
        }
    }

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        string toolName = Name;
        try
        {
            var rawArgs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (arguments != null)
            {
                foreach (var (k, v) in arguments)
                {
                    rawArgs[k] = v;
                }
            }

            var normalized = _dispatcher.NormalizeCall(Name, rawArgs);
            toolName = normalized.CanonicalToolName;

            if (!string.Equals(normalized.CanonicalToolName, Name, StringComparison.OrdinalIgnoreCase) &&
                _dispatcher.TryGetRegisteredTool(normalized.CanonicalToolName, out var targetTool) &&
                targetTool != null)
            {
                return await targetTool.InvokeAsync(normalized.Arguments, cancellationToken).ConfigureAwait(false);
            }

            return await base.InvokeCoreAsync(normalized.Arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var innerMsg = ex.InnerException != null ? $" (Inner: {ex.InnerException.Message})" : "";
            return $"Error executing tool '{toolName}': {ex.Message}{innerMsg}";
        }
    }
}

/// <summary>
/// Extension methods for wrapping <see cref="AIFunction"/> instances in <see cref="LenientAIFunction"/>.
/// </summary>
public static class LenientAIFunctionExtensions
{
    /// <summary>
    /// Wraps an <see cref="AIFunction"/> in a <see cref="LenientAIFunction"/> that normalizes parameters before invocation.
    /// </summary>
    public static AIFunction AsLenient(this AIFunction function, LenientToolDispatcher dispatcher)
        => new LenientAIFunction(function, dispatcher);

    /// <summary>
    /// Wraps a collection of <see cref="AIFunction"/> instances in <see cref="LenientAIFunction"/> decorators.
    /// </summary>
    public static IEnumerable<AIFunction> AsLenient(this IEnumerable<AIFunction> functions, LenientToolDispatcher dispatcher)
        => functions.Select(f => f.AsLenient(dispatcher));
}

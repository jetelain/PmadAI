using Microsoft.Extensions.AI;

namespace Pmad.AI.Tools;

/// <summary>
/// Delegating chat client middleware that normalizes <see cref="FunctionCallContent"/> items
/// using <see cref="LenientToolDispatcher"/> so that downstream <c>FunctionInvokingChatClient</c> resolves canonical tools.
/// </summary>
public class LenientToolDispatchChatClient : DelegatingChatClient
{
    private readonly LenientToolDispatcher _dispatcher;

    public LenientToolDispatchChatClient(IChatClient innerClient, LenientToolDispatcher dispatcher)
        : base(innerClient)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    private void RegisterRequestTools(ChatOptions? options)
    {
        if (options?.Tools is { Count: > 0 })
        {
            _dispatcher.RegisterTools(options.Tools.OfType<AIFunction>());
        }
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ChatOptions();
        options.SetToolDispatcher(_dispatcher);
        if (options.ConversationId != null)
        {
            _dispatcher.SessionId = options.ConversationId;
        }
        RegisterRequestTools(options);
        var response = await base.GetResponseAsync(chatMessages, options, cancellationToken);
        if (response?.Messages != null)
        {
            foreach (var msg in response.Messages)
            {
                NormalizeMessageToolCalls(msg);
            }
        }
        return response!;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new ChatOptions();
        options.SetToolDispatcher(_dispatcher);
        if (options.ConversationId != null)
        {
            _dispatcher.SessionId = options.ConversationId;
        }
        RegisterRequestTools(options);
        await foreach (var update in base.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
        {
            if (update.Contents != null)
            {
                for (int i = 0; i < update.Contents.Count; i++)
                {
                    if (update.Contents[i] is FunctionCallContent fcc)
                    {
                        if (string.IsNullOrWhiteSpace(fcc.Name))
                        {
                            continue;
                        }

                        try
                        {
                            var dispatched = _dispatcher.NormalizeCall(fcc);
                            update.Contents[i] = new FunctionCallContent(fcc.CallId, dispatched.CanonicalToolName, dispatched.Arguments);
                        }
                        catch (KeyNotFoundException)
                        {
                            // In streaming, arguments might not be fully assembled yet for translations.
                        }
                    }
                }
            }
            yield return update;
        }
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(LenientToolDispatcher))
        {
            return _dispatcher;
        }

        if (serviceType == typeof(LenientDispatchTelemetry) && _dispatcher.Telemetry != null)
        {
            return _dispatcher.Telemetry;
        }

        return base.GetService(serviceType, serviceKey);
    }

    private void NormalizeMessageToolCalls(ChatMessage message)
    {
        if (message.Contents == null) return;

        for (int i = 0; i < message.Contents.Count; i++)
        {
            if (message.Contents[i] is FunctionCallContent fcc && !string.IsNullOrWhiteSpace(fcc.Name))
            {
                var dispatched = _dispatcher.NormalizeCall(fcc);
                message.Contents[i] = new FunctionCallContent(fcc.CallId, dispatched.CanonicalToolName, dispatched.Arguments);
            }
        }
    }
}

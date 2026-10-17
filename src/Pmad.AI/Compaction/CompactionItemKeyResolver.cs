using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Compaction;

/// <summary>
/// Helper for resolving session IDs and computing canonical, deterministic item keys for conversation compaction.
/// Trim-safe and reflection-free.
/// </summary>
internal static class CompactionItemKeyResolver
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;
    private const int HashWindowLimit = 4096;

    public static string ResolveSessionId(ChatOptions? options, Func<ChatOptions?, string>? customResolver)
    {
        if (customResolver != null)
        {
            var res = customResolver(options);
            if (!string.IsNullOrWhiteSpace(res)) return res;
        }

        if (!string.IsNullOrWhiteSpace(options?.ConversationId))
        {
            return options.ConversationId;
        }

        if (options?.AdditionalProperties != null &&
            options.AdditionalProperties.TryGetValue("pmad.sessionId", out var sid) &&
            sid != null)
        {
            return sid.ToString() ?? "default";
        }

        return "default";
    }

    public static string ResolveItemKey(ChatMessage message, SummarizationTarget target, string targetText, string? callId = null)
    {
        // 1. Tool result CallId (per-item identifier)
        if (target == SummarizationTarget.ToolResult && !string.IsNullOrWhiteSpace(callId))
        {
            return callId;
        }

        // 2. Explicit message ID
        if (message.AdditionalProperties != null &&
            message.AdditionalProperties.TryGetValue("id", out var idVal) &&
            idVal != null)
        {
            var idStr = idVal.ToString();
            if (!string.IsNullOrWhiteSpace(idStr))
            {
                return idStr;
            }
        }

        // 3. Deterministic 64-bit FNV-1a hash over target content + role
        var prefix = target == SummarizationTarget.ThinkingMonologue ? "assistant:" : "tool:";
        return ComputeFnv1aKey(prefix, targetText);
    }

    private static string ComputeFnv1aKey(string rolePrefix, string text)
    {
        ulong hash = FnvOffsetBasis;

        // Hash role prefix
        foreach (var c in rolePrefix)
        {
            hash ^= c;
            hash *= FnvPrime;
        }

        // Hash text length
        hash ^= (ulong)text.Length;
        hash *= FnvPrime;

        if (text.Length <= HashWindowLimit * 2)
        {
            // Hash entire text
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= FnvPrime;
            }
        }
        else
        {
            // Hash first 4096 chars
            for (int i = 0; i < HashWindowLimit; i++)
            {
                hash ^= text[i];
                hash *= FnvPrime;
            }

            // Hash last 4096 chars
            int startOfTail = text.Length - HashWindowLimit;
            for (int i = startOfTail; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= FnvPrime;
            }
        }

        return hash.ToString("x16");
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Native;
using Microsoft.Extensions.Logging;

namespace Pmad.AI.LlamaSharp;

/// <summary>
/// A resilient specialization of <see cref="InteractiveExecutor"/> that chunks large prompt inputs
/// during pre-tokenization to prevent unmanaged stack overflows (0xC0000005) in llama.cpp's BPE regex engine.
/// </summary>
internal sealed class SafeInteractiveExecutor : InteractiveExecutor
{
    private bool _isFirstPromptRun = true;
    private const int MaxSafeChunkChars = 8192;

    public SafeInteractiveExecutor(LLamaContext context, ILogger? logger = null)
        : base(context, logger)
    {
    }

    public SafeInteractiveExecutor(LLamaContext context, MtmdWeights clipModel, ILogger? logger = null)
        : base(context, clipModel, logger)
    {
    }

    protected override Task PreprocessInputs(string? text, InferStateArgs args, CancellationToken cancellationToken = default)
    {
        if (IsMultiModal || string.IsNullOrEmpty(text) || text.Length <= MaxSafeChunkChars)
        {
            _isFirstPromptRun = false;
            return base.PreprocessInputs(text, args, cancellationToken);
        }

        // Handle large text by chunking along newline boundaries to prevent native stack overflow
        if (_isFirstPromptRun)
        {
            _embed_inps = SafeTokenize(text, addBos: true);
            _isFirstPromptRun = false;
        }
        else
        {
            if (!text.EndsWith('\n'))
            {
                text += "\n";
            }

            var lineInput = SafeTokenize(text, addBos: false);
            _embed_inps.AddRange(lineInput);
            args.RemainedTokens -= lineInput.Count;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Safely tokenizes large strings in chunks along newline boundaries so that no single call
    /// to native llama_tokenize exceeds the safe buffer limit.
    /// </summary>
    private List<LLamaToken> SafeTokenize(string text, bool addBos)
    {
        var result = new List<LLamaToken>();
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        int startIndex = 0;
        bool isFirst = addBos;

        while (startIndex < text.Length)
        {
            int length = Math.Min(MaxSafeChunkChars, text.Length - startIndex);
            if (startIndex + length < text.Length)
            {
                int lastNewline = text.LastIndexOf('\n', startIndex + length - 1, length);
                if (lastNewline > startIndex)
                {
                    length = lastNewline - startIndex + 1;
                }
            }

            var chunk = text.Substring(startIndex, length);
            var chunkTokens = Context.Tokenize(chunk, addBos: isFirst, special: true);
            result.AddRange(chunkTokens);

            isFirst = false;
            startIndex += length;
        }

        return result;
    }
}

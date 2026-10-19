using LLama;
using LLama.Common;
using Pmad.AI.LlamaSharp;
using Pmad.AI.Tokens;
using Xunit.Abstractions;

namespace Pmad.AI.LlamaSharp.Tests;

public class TokenCalibrationTests
{
    private readonly ITestOutputHelper _output;

    public TokenCalibrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TestVocabOnlyTokenization()
    {
        var modelPaths = new[]
        {
            @"C:\Workspace\models\qwen3.5-2b\qwen3.5-2b-ud-q4_k_xl.gguf",
            @"C:\Workspace\models\gemma-4-e2b\gemma-4-e2b-it-ud-q4_k_xl.gguf",
            @"C:\Users\Julien\source\repos\jetelain\wiki\ai\models\Qwen3.6-35B-A3B\Qwen3.6-35B-A3B-UD-Q4_K_XL.gguf",
            @"C:\Users\Julien\source\repos\jetelain\wiki\ai\models\Qwen3.8-27B\Qwen3.8-27B-UD-Q4_K_XL.gguf"
        };

        var sampleText = "Hello, world! How does Qwen tokenize this sentence with 123 numbers and punctuation? L'intelligence artificielle est fascinante.";
        var metrics = HeuristicTokenEstimator.Analyze(sampleText);
        _output.WriteLine($"Sample metrics: Words={metrics.Words}, Symbols={metrics.Symbols}, NonAscii={metrics.NonAscii}, Chars={metrics.CharCount}");

        foreach (var path in modelPaths)
        {
            if (!File.Exists(path))
            {
                _output.WriteLine($"Skipping missing: {path}");
                continue;
            }

            var modelParams = new ModelParams(path)
            {
                VocabOnly = true,
                ContextSize = 1024,
                GpuLayerCount = 0
            };

            using var weights = LLamaWeights.LoadFromFile(modelParams);
            using var context = weights.CreateContext(modelParams);

            var tokens = context.Tokenize(sampleText, addBos: false, special: false);
            _output.WriteLine($"Model: {Path.GetFileName(path)} => Actual tokens: {tokens.Length}");
        }
    }
}

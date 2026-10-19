using System.Diagnostics;
using System.Text;
using LLama;
using LLama.Common;
using Pmad.AI.Tokens;

namespace Pmad.AI.Benchmark.Tools;

/// <summary>
/// Diagnostic runner that extracts primitive structural metrics (words, symbols, non-ASCII, chars)
/// alongside ground-truth token counts directly from local GGUF tokenizers in VocabOnly mode.
/// Produces a structured CSV dataset for statistical parameter tuning via WSL2 Python.
/// </summary>
public static class TokenCalibrationRunner
{
    public sealed record ModelTarget(string Family, string Name, string FilePath);

    public static void Run(string? outputCsvPath = null)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
╔═══════════════════════════════════════════════════════════════════╗
║       Pmad.AI - Heuristic Token Estimator Calibration Tool        ║
╚═══════════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        outputCsvPath ??= Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "token_calibration_data.csv"));
        Console.WriteLine($"Output CSV: {outputCsvPath}\n");

        var targets = DiscoverAvailableModels();
        if (targets.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Warning: No local GGUF models discovered. Please check model directories.");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Discovered {targets.Count} model(s) for calibration:");
        foreach (var t in targets)
        {
            Console.WriteLine($"  • [{t.Family}] {t.Name} ({Path.GetFileName(t.FilePath)})");
        }
        Console.WriteLine();

        var corpus = BuildCalibrationCorpus();
        Console.WriteLine($"Loaded calibration corpus with {corpus.Count} diverse text samples across 7 categories.\n");

        var sb = new StringBuilder();
        sb.AppendLine("model_family,model_name,category,sample_id,words,symbols,non_ascii,chars,actual_tokens");

        int totalObservations = 0;
        var swTotal = Stopwatch.StartNew();

        foreach (var target in targets)
        {
            Console.Write($"Profiling {target.Name} (VocabOnly)... ");
            var swModel = Stopwatch.StartNew();

            try
            {
                var modelParams = new ModelParams(target.FilePath)
                {
                    VocabOnly = true,
                    ContextSize = 4096,
                    GpuLayerCount = 0
                };

                using var weights = LLamaWeights.LoadFromFile(modelParams);
                using var context = weights.CreateContext(modelParams);

                int sampleIndex = 0;
                foreach (var (category, text) in corpus)
                {
                    sampleIndex++;
                    var metrics = HeuristicTokenEstimator.Analyze(text);
                    var tokens = context.Tokenize(text, addBos: false, special: false);
                    int actualTokens = tokens.Length;

                    sb.AppendLine($"\"{target.Family}\",\"{target.Name}\",\"{category}\",{sampleIndex},{metrics.Words},{metrics.Symbols},{metrics.NonAscii},{metrics.CharCount},{actualTokens}");
                    totalObservations++;
                }

                swModel.Stop();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Done ({swModel.ElapsedMilliseconds} ms, {corpus.Count} samples)");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed: {ex.Message}");
                Console.ResetColor();
            }
        }

        File.WriteAllText(outputCsvPath, sb.ToString(), Encoding.UTF8);
        swTotal.Stop();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\nSuccessfully saved {totalObservations} observations to: {outputCsvPath}");
        Console.WriteLine($"Total execution duration: {swTotal.Elapsed.TotalSeconds:F2} seconds.");
        Console.ResetColor();
    }

    public static List<ModelTarget> DiscoverAvailableModels()
    {
        var targets = new List<ModelTarget>();
        var candidates = new (string Family, string Name, string Path)[]
        {
            // Qwen 3.5 models
            ("Qwen3.5", "qwen3.5-2b", @"C:\Workspace\models\qwen3.5-2b\qwen3.5-2b-ud-q4_k_xl.gguf"),
            ("Qwen3.5", "qwen3.5-4b", @"C:\Workspace\models\qwen3.5-4b\qwen3.5-4b-ud-q4_k_xl.gguf"),
            ("Qwen3.5", "qwen3.5-9b", @"C:\Workspace\models\qwen3.5-9b\qwen3.5-9b-ud-q4_k_xl.gguf"),

            // Qwen 3.6 models
            ("Qwen3.6", "qwen3.6-35b-a3b", @"C:\Users\Julien\source\repos\jetelain\wiki\ai\models\Qwen3.6-35B-A3B\Qwen3.6-35B-A3B-UD-Q4_K_XL.gguf"),
            ("Qwen3.6", "qwen3.6-27b", @"C:\Users\Julien\source\repos\jetelain\wiki\ai\models\Qwen3.6-27B\Qwen3.6-27B-UD-Q4_K_XL.gguf"),

            // Qwen 3.8 models
            ("Qwen3.8", "qwen3.8-27b", @"C:\Users\Julien\source\repos\jetelain\wiki\ai\models\Qwen3.8-27B\Qwen3.8-27B-UD-Q4_K_XL.gguf"),

            // Gemma 4 models
            ("Gemma4", "gemma-4-e2b", @"C:\Workspace\models\gemma-4-e2b\gemma-4-e2b-it-ud-q4_k_xl.gguf"),
            ("Gemma4", "gemma-4-e4b", @"C:\Workspace\models\gemma-4-e4b\gemma-4-e4b-it-ud-q4_k_xl.gguf"),
            ("Gemma4", "gemma-4-12b", @"C:\Workspace\models\gemma-4-12b\gemma-4-12b-it-ud-q4_k_xl.gguf"),
            ("Gemma4", "gemma-4-26b-a4b", @"C:\Users\Julien\source\repos\jetelain\wiki\ai\models\Gemma-4-26B-A4B\gemma-4-26B-A4B-it-UD-Q8_K_XL.gguf"),
            ("Gemma4", "gemma-4-31b", @"C:\Users\Julien\source\repos\jetelain\wiki\ai\models\Gemma-4-31B\gemma-4-31B-it-UD-Q4_K_XL.gguf"),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c.Path))
            {
                targets.Add(new ModelTarget(c.Family, c.Name, c.Path));
            }
        }

        return targets;
    }

    public static List<(string Category, string Text)> BuildCalibrationCorpus()
    {
        var list = new List<(string Category, string Text)>();

        // Category 1: C# Source Code
        list.Add(("code_csharp", "public sealed class WeatherReport { public string City { get; set; } = string.Empty; public double Temperature { get; set; } }"));
        list.Add(("code_csharp", "var results = items.Where(x => x.IsActive && x.Score >= 0.75).OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.Title }).ToList();"));
        list.Add(("code_csharp", @"
public async Task<int> ProcessBatchAsync(IReadOnlyList<TaskItem> tasks, CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(tasks);
    int completed = 0;
    foreach (var task in tasks)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await task.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref completed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ""Task {TaskId} failed with error: {Message}"", task.Id, ex.Message);
        }
    }
    return completed;
}"));
        list.Add(("code_csharp", "namespace Pmad.AI.Tokens; [JsonSerializable(typeof(List<Dictionary<string, object?>>))] public partial class SourceContext : JsonSerializerContext {}"));
        list.Add(("code_csharp", "unsafe void CopyMemory(byte* src, byte* dst, int length) { Unsafe.CopyBlock(dst, src, (uint)length); }"));

        // Category 2: Python Code
        list.Add(("code_python", "def calculate_statistics(numbers: list[float]) -> dict[str, float]:\n    return {'mean': sum(numbers) / len(numbers), 'min': min(numbers), 'max': max(numbers)}"));
        list.Add(("code_python", @"
import numpy as np
import scipy.optimize as opt

def fit_regression(X: np.ndarray, y: np.ndarray) -> np.ndarray:
    result = opt.lsq_linear(X, y, bounds=(0, np.inf), method='bvls')
    if not result.success:
        raise RuntimeError(f'Optimization failed: {result.message}')
    return result.x
"));
        list.Add(("code_python", "class AgentWorker(BaseWorker):\n    def __init__(self, name: str, max_retries: int = 3):\n        super().__init__(name)\n        self.max_retries = max_retries\n        self._history = []"));
        list.Add(("code_python", "[x**2 for x in range(100) if x % 3 == 0 and x % 5 != 0]"));

        // Category 3: Web Code (TS, JS, HTML, CSS)
        list.Add(("code_web", "export const useAuth = () => { const [user, setUser] = useState<User | null>(null); return { user, isAuthenticated: !!user }; };"));
        list.Add(("code_web", "<div className=\"flex flex-col items-center justify-between p-4 rounded-xl bg-slate-900 border border-slate-800 shadow-md\">"));
        list.Add(("code_web", "document.querySelectorAll('.btn-submit').forEach(btn => btn.addEventListener('click', (e) => e.preventDefault()));"));
        list.Add(("code_web", "@media (min-width: 1024px) { .sidebar-container { display: flex; width: 280px; flex-shrink: 0; } }"));

        // Category 4: Data & Structured text (SQL, JSON, YAML)
        list.Add(("code_data", "SELECT u.id, u.username, COUNT(o.id) AS total_orders, SUM(o.amount) AS total_spent FROM users u LEFT JOIN orders o ON u.id = o.user_id WHERE u.created_at >= '2025-01-01' GROUP BY u.id, u.username HAVING COUNT(o.id) > 5 ORDER BY total_spent DESC LIMIT 50;"));
        list.Add(("code_data", "{\"status\": \"success\", \"code\": 200, \"data\": {\"user_id\": 10928, \"roles\": [\"admin\", \"auditor\"], \"flags\": {\"beta_enabled\": true, \"rate_limit\": 1000}}}"));
        list.Add(("code_data", "version: '3.8'\nservices:\n  agent-engine:\n    image: pmad/agent:latest\n    environment:\n      - PMAD_ENV=production\n      - LOG_LEVEL=info\n    ports:\n      - \"8080:8080\"\n    restart: unless-stopped"));
        list.Add(("code_data", "| Metric | Baseline | Optimized | Delta | Ratio |\n|---|---|---|---|---|\n| Latency | 45.2ms | 12.1ms | -33.1ms | 3.73x |\n| Memory | 256MB | 48MB | -208MB | 5.33x |\n| Heap Alloc | 12.4MB | 0.0MB | -12.4MB | 0-alloc |"));

        // Category 5: AI & Agent Formats (ChatML, System Prompts, Tool Invocations)
        list.Add(("ai_dialogue", "<|im_start|>system\nYou are a high-performance coding assistant. Follow instructions carefully, verify safety boundaries, and format responses with precision.<|im_end|>\n<|im_start|>user\nFind the optimal regression coefficients for token heuristics.<|im_end|>\n<|im_start|>assistant\n<think>\nWe need to run non-negative least squares over the extracted feature matrix.\n</think>\nHere is the formulation:"));
        list.Add(("ai_dialogue", "{\"type\":\"function\",\"function\":{\"name\":\"execute_bash_command\",\"description\":\"Executes a shell command in the container environment.\",\"parameters\":{\"type\":\"object\",\"properties\":{\"command\":{\"type\":\"string\",\"description\":\"The shell command to run.\"}},\"required\":[\"command\"]}}}"));
        list.Add(("ai_dialogue", "{\"role\":\"assistant\",\"tool_calls\":[{\"id\":\"call_98765\",\"type\":\"function\",\"function\":{\"name\":\"query_database\",\"arguments\":\"{\\\"query\\\":\\\"SELECT * FROM metrics WHERE timestamp > 1700000000\\\",\\\"timeout\\\":30}\"}}]}"));
        list.Add(("ai_dialogue", "{\"role\":\"tool\",\"tool_call_id\":\"call_98765\",\"name\":\"query_database\",\"content\":\"{\\\"rows_returned\\\":42,\\\"execution_time_ms\\\":14.2,\\\"status\\\":\\\"ok\\\"}\"}"));

        // Category 6: Natural Language & Multilingual Prose
        list.Add(("prose_multilingual", "The architecture of modern language models relies heavily on byte-pair encoding (BPE) or WordPiece tokenizers, which map subword character sequences to discrete vocabulary indices. In scenarios requiring low memory footprints, heuristic estimation avoids loading tens of thousands of tokens into heap memory."));
        list.Add(("prose_multilingual", "L'intelligence artificielle générative et les modèles de langage de petite taille (SLM) permettent une exécution efficace en local. Les accents français tels que l'accent aigu (é), l'accent grave (è, à), l'accent circonflexe (ê, î, ô, û) et la cédille (ç) sont traités soit en caractères distincts, soit via des paires de multiplets UTF-8."));
        list.Add(("prose_multilingual", "Die Optimierung von Inferenzmodellen erfordert eine präzise Schätzung des Speicherbedarfs für den Kontext. Deutsche Komposita wie 'Donaudampfschifffahrtselektrizitätenhauptbetriebswerkbauunterbeamtengesellschaft' stellen Tokenizer vor besondere Herausforderungen."));
        list.Add(("prose_multilingual", "El procesamiento del lenguaje natural permite a las herramientas de inteligencia artificial comprender el contexto y responder con gran fidelidad a las instrucciones complejas del usuario."));
        list.Add(("prose_multilingual", "大型语言模型（LLM）与小型语言模型（SLM）在代码生成与自然语言理解方面表现优异。分词器将连续的文本分割成有意义的词元标记，从而进行神经网络的高效计算。"));
        list.Add(("prose_multilingual", "自然言語処理におけるトークナイザーは、入力文をトークン列に変換する重要な構成要素です。日本語の形態素解析やサブワード分割が精度に大きく影響します。"));
        list.Add(("prose_multilingual", "Генеративный искусственный интеллект открывает новые возможности для автоматизации программирования и анализа сложных систем данных."));
        list.Add(("prose_multilingual", "Emoji and decorative symbols: 🚀 🤖 ⚡ 💡 🔧 📈 🔥 🌟 🎯 ✅ ❌ ⚠️ 🛡️ 📦 🔍 📊. How do tokenizers handle sequences with mixed emojis?"));

        // Category 7: Dense & Adversarial samples (for CharFloorFactor calibration)
        list.Add(("dense_adversarial", "aGVsbG8gd29ybGQgdGhpcyBpcyBhIHNhbXBsZSBiYXNlNjQgZW5jb2RlZCBzdHJpbmcgd2hpY2ggY29udGFpbnMgbm8gd2hpdGVzcGFjZSBhdCBhbGwgYW5kIGhhcyBoaWdoIGVudHJvcHk="));
        list.Add(("dense_adversarial", "4f53cda18c2baa0c0354bb5f9a3ecbe5ed12abfc94e1234567890abcdef1234567890abcdef1234567890abcdef"));
        list.Add(("dense_adversarial", "https://api.pmad.net/v1/workspaces/proj-98213/runs/run-abc-def-1234/artifacts/logs/benchmark_results.json?token=xyz9876543210&format=json&compress=gzip"));
        list.Add(("dense_adversarial", "C:\\Users\\Julien\\source\\repos\\jetelain\\PmadAI\\src\\Pmad.AI.LlamaSharp\\Dialects\\ChatDialect.cs:line 184 at Pmad.AI.LlamaSharp.LocalModelChatFormatter.FormatPrompt(IReadOnlyList`1 messages, String dialectName, LlamaSharpOptions options)"));
        list.Add(("dense_adversarial", "===================================================================================================="));
        list.Add(("dense_adversarial", "////////////////////////////////////////////////////////////////////////////////////////////////////"));
        list.Add(("dense_adversarial", "****************************************************************************************************"));
        list.Add(("dense_adversarial", "!@#$%^&*()_+{}|:\"<>?~`-=[]\\;',./!@#$%^&*()_+{}|:\"<>?~`-=[]\\;',./!@#$%^&*()_+{}|:\"<>?~`-=[]\\;',./"));
        list.Add(("dense_adversarial", "123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890"));

        // Additional real-world mixed paragraphs
        list.Add(("prose_multilingual", "In C#, using ReadOnlySpan<char> and ref structs prevents GC heap allocations during fast-path string operations. The method HeuristicTokenEstimator.Analyze scans the character buffer in a single linear pass with O(N) time complexity and O(1) space complexity."));
        list.Add(("ai_dialogue", "<|im_start|>system\nYou are a reasoning agent. Respond using JSON format only.\n{\n  \"action\": \"string\",\n  \"parameters\": {}\n}\n<|im_end|>\n<|im_start|>user\nCreate a backup of database 'production_db' to '/mnt/backup/db_20261019.sql.gz'.<|im_end|>\n<|im_start|>assistant\n```json\n{\n  \"action\": \"backup_database\",\n  \"parameters\": {\n    \"database\": \"production_db\",\n    \"destination\": \"/mnt/backup/db_20261019.sql.gz\",\n    \"compression\": \"gzip\"\n  }\n}\n```<|im_end|>"));
        list.Add(("code_csharp", "var query = dbContext.Orders.Include(o => o.Items).Where(o => o.Status == OrderStatus.Pending && o.CreatedAt < DateTime.UtcNow.AddDays(-7)).AsNoTracking();"));

        return list;
    }
}

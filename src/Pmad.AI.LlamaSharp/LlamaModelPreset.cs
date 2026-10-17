namespace Pmad.AI.LlamaSharp;

/// <summary>
/// Verified preset metadata for local GGUF models executed via LLamaSharp.
/// Self-hosted at https://onnx.pmad.net/{Id}/{FileName}.
/// </summary>
public sealed record LlamaModelPreset(
    string Id,
    string Name,
    string FileName,
    string ApproxSize,
    string Description,
    string PromptFormat = "chatml",
    int ContextLength = 49152,
    string? BaseUrl = null,
    IReadOnlyList<string>? Aliases = null,
    string? MmprojFileName = "mmproj-bf16.gguf",
    bool SupportsReasoning = false,
    bool SupportsReasoningEffort = false,
    string? MtpFileName = null
)
{
    /// <summary>
    /// Default hosting endpoint for validated GGUF models.
    /// </summary>
    public const string DefaultHostingBase = "https://onnx.pmad.net";

    /// <summary>
    /// Gets whether this preset has a companion multimodal projector (<c>mmproj-*.gguf</c>).
    /// </summary>
    public bool HasVision => !string.IsNullOrWhiteSpace(MmprojFileName);

    /// <summary>
    /// Gets whether this preset has a companion Multi-Token Prediction (MTP) draft model (<c>mtp-*.gguf</c>).
    /// </summary>
    public bool HasMtp => !string.IsNullOrWhiteSpace(MtpFileName);

    /// <summary>
    /// Base URL with trailing slash for downloading model artifacts.
    /// </summary>
    public string ResolvedBaseUrl => !string.IsNullOrWhiteSpace(BaseUrl)
        ? (BaseUrl.EndsWith('/') ? BaseUrl : BaseUrl + "/")
        : $"{DefaultHostingBase}/{Id}/";

    /// <summary>
    /// Full URL to download the model's main GGUF weights.
    /// </summary>
    public string DownloadUrl => $"{ResolvedBaseUrl}{FileName}";

    /// <summary>
    /// Full URL to download the companion vision projector, or null if vision is not supported.
    /// </summary>
    public string? MmprojDownloadUrl => HasVision ? $"{ResolvedBaseUrl}{MmprojFileName}" : null;

    /// <summary>
    /// Full URL to download the companion Multi-Token Prediction (MTP) draft model, or null if MTP is not supported.
    /// </summary>
    public string? MtpDownloadUrl => HasMtp ? $"{ResolvedBaseUrl}{MtpFileName}" : null;

    /// <summary>
    /// Collection of verified presets.
    /// </summary>
    public static readonly LlamaModelPreset[] Presets =
    [
        new(
            Id: "qwen3.5-2b",
            Name: "Qwen 3.5 2B (Q4_K_XL)",
            FileName: "qwen3.5-2b-ud-q4_k_xl.gguf",
            ApproxSize: "~1.2 GB",
            Description: "Compact and fast Qwen 3.5 2B instruct GGUF model.",
            PromptFormat: "chatml",
            ContextLength: 49152,
            Aliases: ["qwen3.5-2b-gguf", "qwen3.5-2b-q4", "qwen3.5"],
            SupportsReasoning: true,
            SupportsReasoningEffort: false
        ),
        new(
            Id: "qwen3.5-4b",
            Name: "Qwen 3.5 4B (Q4_K_XL)",
            FileName: "qwen3.5-4b-ud-q4_k_xl.gguf",
            ApproxSize: "~2.7 GB",
            Description: "Balanced Qwen 3.5 4B instruct GGUF model.",
            PromptFormat: "chatml",
            ContextLength: 49152,
            Aliases: ["qwen3.5-4b-gguf", "qwen3.5-4b-q4"],
            SupportsReasoning: true,
            SupportsReasoningEffort: false
        ),
        new(
            Id: "qwen3.5-9b",
            Name: "Qwen 3.5 9B (Q4_K_XL)",
            FileName: "qwen3.5-9b-ud-q4_k_xl.gguf",
            ApproxSize: "~5.6 GB",
            Description: "High-capability Qwen 3.5 9B instruct GGUF model.",
            PromptFormat: "chatml",
            ContextLength: 49152,
            Aliases: ["qwen3.5-9b-gguf", "qwen3.5-9b-q4", "qwen-9b", "qwen3.5-9b-it"],
            SupportsReasoning: true,
            SupportsReasoningEffort: false
        ),
        new(
            Id: "gemma-4-e2b",
            Name: "Gemma 4 E2B (Q4_K_XL)",
            FileName: "gemma-4-e2b-it-ud-q4_k_xl.gguf",
            ApproxSize: "~3.0 GB",
            Description: "Google Gemma 4 E2B instruct GGUF model.",
            PromptFormat: "gemma4",
            ContextLength: 49152,
            Aliases: ["gemma-4-e2b-gguf", "gemma-4-e2b-it", "gemma4-e2b", "gemma-4", "gemma-2b"],
            SupportsReasoning: true,
            SupportsReasoningEffort: false,
            MtpFileName: "mtp-gemma-4-e2b-it.gguf"
        ),
        new(
            Id: "gemma-4-e4b",
            Name: "Gemma 4 E4B (Q4_K_XL)",
            FileName: "gemma-4-e4b-it-ud-q4_k_xl.gguf",
            ApproxSize: "~4.8 GB",
            Description: "Google Gemma 4 E4B instruct GGUF model.",
            PromptFormat: "gemma4",
            ContextLength: 49152,
            Aliases: ["gemma-4-e4b-gguf", "gemma-4-e4b-it", "gemma4-e4b", "gemma-4b"],
            SupportsReasoning: true,
            SupportsReasoningEffort: false,
            MtpFileName: "mtp-gemma-4-e4b-it.gguf"
        ),
        new(
            Id: "gemma-4-12b",
            Name: "Gemma 4 12B (Q4_K_XL)",
            FileName: "gemma-4-12b-it-ud-q4_k_xl.gguf",
            ApproxSize: "~7.2 GB",
            Description: "Google Gemma 4 12B instruct GGUF model.",
            PromptFormat: "gemma4",
            ContextLength: 49152,
            Aliases: ["gemma-4-12b-gguf", "gemma-4-12b-it", "gemma4-12b", "gemma-12b"],
            SupportsReasoning: true,
            SupportsReasoningEffort: false,
            MtpFileName: "mtp-gemma-4-12b-it.gguf"
        )
    ];

    /// <summary>
    /// Recommended default preset: Qwen 3.5 2B.
    /// </summary>
    public static LlamaModelPreset Default => Presets[0];

    /// <summary>
    /// Finds a preset by ID, name, or alias matching.
    /// </summary>
    public static LlamaModelPreset? Find(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;

        var trimmed = identifier.Trim();

        var direct = Presets.FirstOrDefault(p =>
            p.Id.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
            p.FileName.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
            (p.MtpFileName != null && p.MtpFileName.Equals(trimmed, StringComparison.OrdinalIgnoreCase)) ||
            (p.Aliases != null && p.Aliases.Any(a => a.Equals(trimmed, StringComparison.OrdinalIgnoreCase))));

        if (direct != null) return direct;

        if (trimmed.Equals("qwen3.5-2b", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("qwen3.5", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("qwen3", StringComparison.OrdinalIgnoreCase))
        {
            return Presets.FirstOrDefault(p => p.Id == "qwen3.5-2b") ?? Default;
        }

        if (trimmed.Equals("qwen3.5-4b", StringComparison.OrdinalIgnoreCase))
        {
            return Presets.FirstOrDefault(p => p.Id == "qwen3.5-4b");
        }

        if (trimmed.Equals("qwen3.5-9b", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("qwen-9b", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("qwen3.5-9b-it", StringComparison.OrdinalIgnoreCase))
        {
            return Presets.FirstOrDefault(p => p.Id == "qwen3.5-9b");
        }

        if (trimmed.Equals("gemma-4-e2b", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("gemma-4", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("gemma4", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("gemma", StringComparison.OrdinalIgnoreCase))
        {
            return Presets.FirstOrDefault(p => p.Id == "gemma-4-e2b");
        }

        if (trimmed.Equals("gemma-4-e4b", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("gemma4-e4b", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("gemma-4b", StringComparison.OrdinalIgnoreCase))
        {
            return Presets.FirstOrDefault(p => p.Id == "gemma-4-e4b");
        }

        if (trimmed.Equals("gemma-4-12b", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("gemma4-12b", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("gemma-12b", StringComparison.OrdinalIgnoreCase))
        {
            return Presets.FirstOrDefault(p => p.Id == "gemma-4-12b");
        }

        if (trimmed.StartsWith("qwen", StringComparison.OrdinalIgnoreCase))
        {
            return Default;
        }

        return null;
    }

    /// <summary>
    /// Returns the target local GGUF file path under the base models cache directory.
    /// </summary>
    public string GetLocalPath(string baseModelsDir) => Path.Combine(baseModelsDir, FileName);

    /// <summary>
    /// Returns the target local multimodal projector (<c>mmproj-*.gguf</c>) file path under the base models cache directory, or null if vision is not supported.
    /// </summary>
    public string? GetMmprojLocalPath(string baseModelsDir) =>
        HasVision ? Path.Combine(baseModelsDir, $"{Id}-{MmprojFileName}") : null;

    /// <summary>
    /// Returns the target local Multi-Token Prediction (MTP) draft model file path under the base models cache directory, or null if MTP is not supported.
    /// </summary>
    public string? GetMtpLocalPath(string baseModelsDir)
    {
        if (!HasMtp) return null;
        var fileName = MtpFileName!.StartsWith("mtp-", StringComparison.OrdinalIgnoreCase)
            ? MtpFileName
            : $"{Id}-{MtpFileName}";
        return Path.Combine(baseModelsDir, fileName);
    }
}
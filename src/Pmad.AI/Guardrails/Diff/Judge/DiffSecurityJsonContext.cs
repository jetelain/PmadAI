using System.Text.Json.Serialization;

namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Source-generated JSON serializer context for the LLM diff security judge, guaranteeing Native AOT and trimming compliance.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip)]
[JsonSerializable(typeof(JudgeScanResponse))]
[JsonSerializable(typeof(JudgeFindingDto))]
[JsonSerializable(typeof(IReadOnlyList<JudgeFindingDto>))]
[JsonSerializable(typeof(List<JudgeFindingDto>))]
internal partial class DiffSecurityJsonContext : JsonSerializerContext
{
}


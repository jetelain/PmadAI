using System.Text.Json.Serialization;

namespace Pmad.AI.DiffGuard.Cli.Formatting.Sarif;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(SarifLog))]
internal sealed partial class SarifJsonContext : JsonSerializerContext
{
}


using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios.Bfcl;

/// <summary>
/// Dynamic AIFunction implementation that exposes a JSON schema and metadata directly.
/// </summary>
public sealed class DynamicJsonAIFunction : AIFunction
{
    private readonly string _name;
    private readonly string _description;
    private readonly JsonElement _jsonSchema;

    public DynamicJsonAIFunction(string name, string description, JsonElement jsonSchema)
    {
        _name = name;
        _description = description;
        _jsonSchema = jsonSchema.Clone();
    }

    public override string Name => _name;
    public override string Description => _description;
    public override JsonElement JsonSchema => _jsonSchema;

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        return new ValueTask<object?>($"Executed {_name} successfully.");
    }
}

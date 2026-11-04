using System.Text.Json.Serialization;

namespace Pmad.AI.DiffGuard.Cli.Formatting.Sarif;

/// <summary>
/// Root container of a SARIF 2.1.0 document.
/// </summary>
public sealed record SarifLog
{
    [JsonPropertyName("$schema")]
    public string Schema { get; init; } = "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json";

    public string Version { get; init; } = "2.1.0";

    public List<SarifRun> Runs { get; init; } = [];
}

/// <summary>
/// Single run execution within a SARIF log.
/// </summary>
public sealed record SarifRun
{
    public SarifTool Tool { get; init; } = new();

    public List<SarifResult> Results { get; init; } = [];
}

/// <summary>
/// Analysis tool metadata.
/// </summary>
public sealed record SarifTool
{
    public SarifDriver Driver { get; init; } = new();
}

/// <summary>
/// Tool driver component and rule descriptors.
/// </summary>
public sealed record SarifDriver
{
    public string Name { get; init; } = "Pmad.AI.DiffGuard";

    public string Version { get; init; } = "0.1.0-preview";

    public string InformationUri { get; init; } = "https://github.com/jetelain/PmadAI";

    public List<SarifReportingDescriptor> Rules { get; init; } = [];
}

/// <summary>
/// Rule descriptor metadata.
/// </summary>
public sealed record SarifReportingDescriptor
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public SarifMultiformatMessageString ShortDescription { get; init; } = new();

    public SarifMultiformatMessageString? FullDescription { get; init; }

    public SarifMultiformatMessageString? Help { get; init; }

    public SarifReportingConfiguration? DefaultConfiguration { get; init; }
}

/// <summary>
/// Default configuration for a rule.
/// </summary>
public sealed record SarifReportingConfiguration
{
    public string Level { get; init; } = "warning";
}

/// <summary>
/// Message string supporting plain text and markdown.
/// </summary>
public sealed record SarifMultiformatMessageString
{
    public string? Text { get; init; }

    public string? Markdown { get; init; }
}

/// <summary>
/// Single finding or violation.
/// </summary>
public sealed record SarifResult
{
    public string RuleId { get; init; } = string.Empty;

    public string Level { get; init; } = "warning";

    public string? CorrelationGuid { get; init; }

    public Dictionary<string, string>? PartialFingerprints { get; init; }

    public SarifMessage Message { get; init; } = new();

    public List<SarifLocation> Locations { get; init; } = [];
}

/// <summary>
/// Result message.
/// </summary>
public sealed record SarifMessage
{
    public string Text { get; init; } = string.Empty;

    public string? Markdown { get; init; }
}

/// <summary>
/// Primary location of a result.
/// </summary>
public sealed record SarifLocation
{
    public SarifPhysicalLocation PhysicalLocation { get; init; } = new();
}

/// <summary>
/// Physical location specifying file artifact and region.
/// </summary>
public sealed record SarifPhysicalLocation
{
    public SarifArtifactLocation ArtifactLocation { get; init; } = new();

    public SarifRegion Region { get; init; } = new();
}

/// <summary>
/// Artifact file URI.
/// </summary>
public sealed record SarifArtifactLocation
{
    public string Uri { get; init; } = string.Empty;

    public string? UriBaseId { get; init; }
}

/// <summary>
/// Text region (lines and columns).
/// </summary>
public sealed record SarifRegion
{
    public int StartLine { get; init; }

    public int EndLine { get; init; }

    public int? StartColumn { get; init; }

    public int? EndColumn { get; init; }
}


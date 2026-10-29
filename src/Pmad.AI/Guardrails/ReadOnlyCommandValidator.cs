using System.Text;

namespace Pmad.AI.Guardrails;

/// <summary>
/// Shell dialect supported by <see cref="ReadOnlyCommandValidator"/>.
/// </summary>
public enum ShellDialect
{
    Bash,
    PowerShell
}

/// <summary>
/// Configuration options for <see cref="ReadOnlyCommandValidator"/>.
/// </summary>
public class ReadOnlyCommandValidatorOptions
{
    /// <summary>Shell dialect being validated. Default: Bash.</summary>
    public ShellDialect Dialect { get; set; } = ShellDialect.Bash;

    /// <summary>Binaries permitted for execution in Bash dialect. Default: read-only utilities.</summary>
    public HashSet<string> AllowedBinaries { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "find", "rg", "grep", "cat", "ls", "git", "head", "tail", "wc", "cd"
    };

    /// <summary>Cmdlets permitted for execution in PowerShell dialect. Default: read-only cmdlets (aliases prohibited).</summary>
    public HashSet<string> AllowedPowerShellCmdlets { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Get-ChildItem", "Get-Content", "Select-String", "Set-Location", "cd"
    };

    /// <summary>Default-deny git subcommands permitted when git binary is invoked.</summary>
    public HashSet<string> AllowedGitSubcommands { get; set; } = new(StringComparer.Ordinal)
    {
        "status", "log", "diff", "show", "blame", "ls-files", "grep", "rev-parse"
    };

    /// <summary>Whether to block subshells and substitutions ($(), backticks, process substitutions). Default: true.</summary>
    public bool BlockSubshells { get; set; } = true;

    /// <summary>Whether to block file redirection operators (&gt;, &gt;&gt;, &lt;, etc.). Default: true. Safe stderr redirections remain tolerated unless <see cref="AllowStderrRedirection"/> is false.</summary>
    public bool BlockRedirection { get; set; } = true;

    /// <summary>Whether to tolerate safe standard error redirections (2&gt;/dev/null, 2&gt;&amp;1, and in PowerShell 2&gt;$null) even when BlockRedirection is enabled. Default: true.</summary>
    public bool AllowStderrRedirection { get; set; } = true;

    /// <summary>Whether to enforce that all chained/piped commands resolve to allowed binaries/cmdlets. Default: true.</summary>
    public bool BlockPipesToDisallowedBinaries { get; set; } = true;
}

/// <summary>
/// Result of command line validation performed by <see cref="ReadOnlyCommandValidator"/>.
/// </summary>
public sealed record CommandValidationResult(
    bool IsAllowed,
    string? Reason = null,
    string? OffendingSegment = null);

/// <summary>
/// Security validator for shell commands in unprivileged / read-only agent environments.
/// Enforces default-deny binary/cmdlet allowlists, blocks redirection, subshells, variable injection,
/// dangerous binary flags, and command piping to unapproved executables.
/// Trim-safe and Native AOT compatible.
/// </summary>
public class ReadOnlyCommandValidator
{
    private readonly ReadOnlyCommandValidatorOptions _options;

    private static readonly HashSet<string> PowerShellProhibitedAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "ls", "dir", "gci", "cat", "gc", "type", "sls", "curl", "wget", "iwr",
        "echo", "write", "rm", "del", "ri", "cp", "cpi", "mv", "mi", "ren",
        "rni", "mkdir", "ni", "clc", "sc", "ac", "iex", "icm", "saps"
    };

    private static readonly HashSet<string> PowerShellDangerousCmdlets = new(StringComparer.OrdinalIgnoreCase)
    {
        "Invoke-Expression", "Invoke-Command", "Start-Process", "Set-Content",
        "Add-Content", "Out-File", "Remove-Item", "New-Item", "Set-Item",
        "Copy-Item", "Move-Item", "Rename-Item", "Clear-Content", "Invoke-RestMethod",
        "Invoke-WebRequest"
    };

    public ReadOnlyCommandValidator(ReadOnlyCommandValidatorOptions? options = null)
    {
        _options = options ?? new ReadOnlyCommandValidatorOptions();
    }

    /// <summary>
    /// Validates a command line against the configured security policy and dialect.
    /// </summary>
    public CommandValidationResult ValidateCommand(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return new CommandValidationResult(true);
        }

        var unquotedCheck = CheckUnquotedDisallowedPatterns(commandLine);
        if (!unquotedCheck.IsAllowed)
        {
            return unquotedCheck;
        }

        // Fast path for single commands without pipes, semicolons, or boolean chaining
        if (commandLine.IndexOfAny(['|', ';', '&']) < 0)
        {
            var trimmed = commandLine.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return new CommandValidationResult(true);
            }

            var tokenResult = Tokenize(trimmed);
            if (!tokenResult.IsAllowed)
            {
                return new CommandValidationResult(false, tokenResult.Reason, trimmed);
            }

            var tokens = tokenResult.Tokens;
            if (tokens.Count == 0)
            {
                return new CommandValidationResult(true);
            }

            if (_options.Dialect == ShellDialect.Bash)
            {
                var bashResult = ValidateBashSegment(trimmed, tokens);
                if (!bashResult.IsAllowed)
                {
                    return bashResult;
                }
            }
            else
            {
                var psResult = ValidatePowerShellSegment(trimmed, tokens);
                if (!psResult.IsAllowed)
                {
                    return psResult;
                }
            }

            return new CommandValidationResult(true);
        }

        var segmentsResult = SplitCommandSegments(commandLine);
        if (!segmentsResult.IsAllowed)
        {
            return new CommandValidationResult(false, segmentsResult.Reason, commandLine);
        }

        var segments = segmentsResult.Segments;
        if (segments.Count == 0)
        {
            return new CommandValidationResult(true);
        }

        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return new CommandValidationResult(false, "Dangling pipe or empty command segment detected.", segment);
            }

            var tokenResult = Tokenize(trimmed);
            if (!tokenResult.IsAllowed)
            {
                return new CommandValidationResult(false, tokenResult.Reason, segment);
            }

            var tokens = tokenResult.Tokens;
            if (tokens.Count == 0)
            {
                continue;
            }

            if (_options.Dialect == ShellDialect.Bash)
            {
                var bashResult = ValidateBashSegment(trimmed, tokens);
                if (!bashResult.IsAllowed)
                {
                    return bashResult;
                }
            }
            else
            {
                var psResult = ValidatePowerShellSegment(trimmed, tokens);
                if (!psResult.IsAllowed)
                {
                    return psResult;
                }
            }
        }

        return new CommandValidationResult(true);
    }

    private CommandValidationResult CheckUnquotedDisallowedPatterns(string command)
    {
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool isEscaped = false;

        for (int i = 0; i < command.Length; i++)
        {
            char c = command[i];

            if (isEscaped)
            {
                isEscaped = false;
                continue;
            }

            if (_options.Dialect == ShellDialect.Bash)
            {
                if (c == '\\' && !inSingleQuote)
                {
                    isEscaped = true;
                    continue;
                }

                if (c == '\'' && !inDoubleQuote)
                {
                    inSingleQuote = !inSingleQuote;
                    continue;
                }

                if (c == '"' && !inSingleQuote)
                {
                    inDoubleQuote = !inDoubleQuote;
                    continue;
                }

                // Check variable expansion in double quotes or unquoted
                if ((inDoubleQuote || !inSingleQuote) && c == '$')
                {
                    if (i + 1 < command.Length)
                    {
                        char next = command[i + 1];
                        if (next == '(')
                        {
                            if (_options.BlockSubshells)
                            {
                                return new CommandValidationResult(false, "Subshell execution ('$(') is not permitted in read-only mode.", command);
                            }
                        }
                        else if (char.IsLetter(next) || next == '_' || next == '{')
                        {
                            return new CommandValidationResult(false, "Environment variable expansion ('$' or '${') is not permitted in read-only mode.", command);
                        }
                    }
                }

                // Outside quotes
                if (!inSingleQuote && !inDoubleQuote)
                {
                    if (_options.BlockRedirection)
                    {
                        if (c == '>')
                        {
                            if (TryMatchSafeStderrRedirection(command, i, out int nextIdx))
                            {
                                i = nextIdx;
                                continue;
                            }
                            return new CommandValidationResult(false, "Output redirection ('>') is not permitted in read-only mode.", command);
                        }
                        if (c == '<')
                        {
                            if (i + 1 < command.Length && command[i + 1] == '(')
                            {
                                if (_options.BlockSubshells)
                                {
                                    return new CommandValidationResult(false, "Process substitution ('<(') is not permitted in read-only mode.", command);
                                }
                            }
                            if (i + 1 < command.Length && command[i + 1] == '<')
                            {
                                return new CommandValidationResult(false, "Here-doc ('<<') is not permitted in read-only mode.", command);
                            }
                            return new CommandValidationResult(false, "Input redirection ('<') is not permitted in read-only mode.", command);
                        }
                    }

                    if (_options.BlockSubshells)
                    {
                        if (c == '`')
                        {
                            return new CommandValidationResult(false, "Subshell execution ('`') is not permitted in read-only mode.", command);
                        }
                        if (c == '>' && i + 1 < command.Length && command[i + 1] == '(')
                        {
                            return new CommandValidationResult(false, "Process substitution ('>(') is not permitted in read-only mode.", command);
                        }
                    }

                    // Brace expansion check
                    if (c == '{' && command.IndexOf(',', i) > i && command.IndexOf('}', i) > command.IndexOf(',', i))
                    {
                        return new CommandValidationResult(false, "Brace expansion ('{...,...}') is not permitted in read-only mode.", command);
                    }
                }
            }
            else // PowerShell
            {
                if (c == '`' && !inSingleQuote)
                {
                    isEscaped = true;
                    continue;
                }

                if (c == '\'' && !inDoubleQuote)
                {
                    // PowerShell allows doubled '' inside single quotes
                    if (inSingleQuote && i + 1 < command.Length && command[i + 1] == '\'')
                    {
                        i++;
                        continue;
                    }
                    inSingleQuote = !inSingleQuote;
                    continue;
                }

                if (c == '"' && !inSingleQuote)
                {
                    if (inDoubleQuote && i + 1 < command.Length && command[i + 1] == '"')
                    {
                        i++;
                        continue;
                    }
                    inDoubleQuote = !inDoubleQuote;
                    continue;
                }

                // Variable or subexpression check in double quotes or unquoted
                if ((inDoubleQuote || !inSingleQuote) && (c == '$' || c == '@'))
                {
                    if (i + 1 < command.Length)
                    {
                        char next = command[i + 1];
                        if (next == '(')
                        {
                            if (_options.BlockSubshells)
                            {
                                return new CommandValidationResult(false, $"PowerShell subexpression ('{c}(') is not permitted in read-only mode.", command);
                            }
                        }
                        else if (char.IsLetter(next) || next == '_' || next == '{')
                        {
                            return new CommandValidationResult(false, $"PowerShell variable expansion ('{c}') is not permitted in read-only mode.", command);
                        }
                    }
                }

                if (!inSingleQuote && !inDoubleQuote)
                {
                    if (c == '&')
                    {
                        return new CommandValidationResult(false, "PowerShell call operator ('&') is not permitted in read-only mode.", command);
                    }

                    if (_options.BlockRedirection && c == '>')
                    {
                        if (TryMatchSafeStderrRedirection(command, i, out int nextIdx))
                        {
                            i = nextIdx;
                            continue;
                        }
                        return new CommandValidationResult(false, "PowerShell redirection ('>') is not permitted in read-only mode.", command);
                    }
                }
            }
        }

        if (inSingleQuote || inDoubleQuote)
        {
            return new CommandValidationResult(false, "Command contains unclosed quote.", command);
        }

        return new CommandValidationResult(true);
    }

    private (bool IsAllowed, List<string> Segments, string? Reason) SplitCommandSegments(string command)
    {
        var segments = new List<string>();
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool isEscaped = false;
        int start = 0;

        for (int i = 0; i < command.Length; i++)
        {
            char c = command[i];

            if (isEscaped)
            {
                isEscaped = false;
                continue;
            }

            char escapeChar = _options.Dialect == ShellDialect.Bash ? '\\' : '`';
            if (c == escapeChar && !inSingleQuote)
            {
                isEscaped = true;
                continue;
            }

            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote)
            {
                if (c == ';')
                {
                    segments.Add(command.Substring(start, i - start));
                    start = i + 1;
                }
                else if (c == '&' && i + 1 < command.Length && command[i + 1] == '&')
                {
                    segments.Add(command.Substring(start, i - start));
                    start = i + 2;
                    i++;
                }
                else if (c == '&' && _options.Dialect == ShellDialect.Bash)
                {
                    // Single '&' (background) also separates commands in Bash, unless part of descriptor redirection (e.g. 2>&1, >&1)
                    bool isRedirection = false;
                    int prev = i - 1;
                    while (prev >= 0 && (command[prev] == ' ' || command[prev] == '\t'))
                    {
                        prev--;
                    }
                    if (prev >= 0 && command[prev] == '>')
                    {
                        isRedirection = true;
                    }
                    if (i + 1 < command.Length && command[i + 1] == '>')
                    {
                        isRedirection = true;
                    }

                    if (!isRedirection)
                    {
                        segments.Add(command.Substring(start, i - start));
                        start = i + 1;
                    }
                }
                else if (c == '|' && i + 1 < command.Length && command[i + 1] == '|')
                {
                    segments.Add(command.Substring(start, i - start));
                    start = i + 2;
                    i++;
                }
                else if (c == '|')
                {
                    segments.Add(command.Substring(start, i - start));
                    start = i + 1;
                }
                else if (c == '\n')
                {
                    segments.Add(command.Substring(start, i - start));
                    start = i + 1;
                }
            }
        }

        if (start < command.Length)
        {
            segments.Add(command.Substring(start));
        }

        return (true, segments, null);
    }

    private (bool IsAllowed, List<string> Tokens, string? Reason) Tokenize(string segment)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool isEscaped = false;

        for (int i = 0; i < segment.Length; i++)
        {
            char c = segment[i];

            if (isEscaped)
            {
                sb.Append(c);
                isEscaped = false;
                continue;
            }

            char escapeChar = _options.Dialect == ShellDialect.Bash ? '\\' : '`';
            if (c == escapeChar && !inSingleQuote)
            {
                isEscaped = true;
                continue;
            }

            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inSingleQuote && !inDoubleQuote)
            {
                if (sb.Length > 0)
                {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                }
                continue;
            }

            sb.Append(c);
        }

        if (inSingleQuote || inDoubleQuote)
        {
            return (false, tokens, "Command segment contains unclosed quote.");
        }

        if (sb.Length > 0)
        {
            tokens.Add(sb.ToString());
        }

        return (true, tokens, null);
    }

    private CommandValidationResult ValidateBashSegment(string rawSegment, List<string> tokens)
    {
        var rawCmd = tokens[0];
        if (rawCmd.Contains('/') || rawCmd.Contains('\\'))
        {
            var clean = rawCmd.Replace('\\', '/');
            if (!clean.StartsWith("/bin/", StringComparison.Ordinal) &&
                !clean.StartsWith("/usr/bin/", StringComparison.Ordinal))
            {
                return new CommandValidationResult(
                    false,
                    $"Path-based command execution ('{rawCmd}') outside standard system paths (/bin, /usr/bin) is not permitted in read-only mode.",
                    rawSegment);
            }
        }

        var binary = NormalizeBinaryName(rawCmd);

        if (!_options.AllowedBinaries.Contains(binary))
        {
            return new CommandValidationResult(
                false,
                $"Command '{binary}' is not permitted in read-only mode. Allowed binaries: {string.Join(", ", _options.AllowedBinaries)}.",
                rawSegment);
        }

        if (string.Equals(binary, "find", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 1; i < tokens.Count; i++)
            {
                var arg = tokens[i];
                if (arg is "-exec" or "-execdir" or "-ok" or "-okdir" or "-delete" or "-fprint" or "-fprint0" or "-fprintf" or "-fls")
                {
                    return new CommandValidationResult(false, $"Dangerous find flag '{arg}' is not permitted.", rawSegment);
                }
            }
        }
        else if (string.Equals(binary, "rg", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 1; i < tokens.Count; i++)
            {
                var arg = tokens[i];
                if (arg is "--pre" or "--hostname-bin" || arg.StartsWith("--pre=", StringComparison.Ordinal) || arg.StartsWith("--pre-glob=", StringComparison.Ordinal))
                {
                    return new CommandValidationResult(false, $"Dangerous rg flag '{arg}' is not permitted.", rawSegment);
                }
            }
        }
        else if (string.Equals(binary, "sed", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 1; i < tokens.Count; i++)
            {
                var arg = tokens[i];
                if (arg is "-i" or "--in-place" || arg.StartsWith("-i", StringComparison.Ordinal) || arg.StartsWith("--in-place=", StringComparison.Ordinal))
                {
                    return new CommandValidationResult(false, $"Mutating sed in-place flag '{arg}' is not permitted.", rawSegment);
                }
            }
        }
        else if (string.Equals(binary, "git", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateGitCommand(rawSegment, tokens);
        }

        return new CommandValidationResult(true);
    }

    private CommandValidationResult ValidateGitCommand(string rawSegment, List<string> tokens)
    {
        int i = 1;
        while (i < tokens.Count)
        {
            var token = tokens[i];
            if (token.StartsWith("-c", StringComparison.Ordinal) ||
                token.StartsWith("--config-env", StringComparison.Ordinal) ||
                token.StartsWith("--exec-path", StringComparison.Ordinal) ||
                token.StartsWith("--upload-pack", StringComparison.Ordinal) ||
                token is "--paginate")
            {
                return new CommandValidationResult(false, $"Dangerous git global flag '{token}' is not permitted.", rawSegment);
            }

            if (token is "-C" or "--work-tree")
            {
                i += 2;
                continue;
            }

            if (token.StartsWith("-"))
            {
                i++;
                continue;
            }

            break;
        }

        if (i >= tokens.Count)
        {
            // e.g. "git --version"
            return new CommandValidationResult(true);
        }

        var subcommand = tokens[i];
        if (!_options.AllowedGitSubcommands.Contains(subcommand))
        {
            return new CommandValidationResult(
                false,
                $"Git subcommand '{subcommand}' is not permitted in read-only mode. Allowed subcommands: {string.Join(", ", _options.AllowedGitSubcommands)}.",
                rawSegment);
        }

        // Subcommand-specific option validation
        for (int j = i + 1; j < tokens.Count; j++)
        {
            var arg = tokens[j];
            if (arg is "--output" || arg.StartsWith("--output=", StringComparison.Ordinal))
            {
                return new CommandValidationResult(false, $"Git '{subcommand}' option '{arg}' writes to a file and is prohibited.", rawSegment);
            }
            if (arg is "--ext-diff" || arg.StartsWith("--ext-diff", StringComparison.Ordinal))
            {
                return new CommandValidationResult(false, $"Git '{subcommand}' option '{arg}' executes external binaries and is prohibited.", rawSegment);
            }
            if (subcommand == "grep" &&
                (arg.StartsWith("--open-files-in-pager", StringComparison.Ordinal) ||
                 (arg.StartsWith("-O", StringComparison.Ordinal) && !arg.StartsWith("--", StringComparison.Ordinal))))
            {
                return new CommandValidationResult(false, $"Git '{subcommand}' option '{arg}' executes an external pager and is prohibited.", rawSegment);
            }
        }

        return new CommandValidationResult(true);
    }

    private CommandValidationResult ValidatePowerShellSegment(string rawSegment, List<string> tokens)
    {
        var rawCmd = tokens[0];
        if (rawCmd.Contains('/') || rawCmd.Contains('\\'))
        {
            return new CommandValidationResult(
                false,
                $"Path-based execution ('{rawCmd}') is not permitted in read-only PowerShell mode. Only canonical cmdlets are allowed.",
                rawSegment);
        }

        var cmdlet = NormalizeBinaryName(rawCmd);

        if (PowerShellProhibitedAliases.Contains(cmdlet))
        {
            return new CommandValidationResult(
                false,
                $"PowerShell alias '{cmdlet}' is prohibited. Use approved canonical cmdlets ({string.Join(", ", _options.AllowedPowerShellCmdlets)}).",
                rawSegment);
        }

        if (PowerShellDangerousCmdlets.Contains(cmdlet))
        {
            return new CommandValidationResult(
                false,
                $"PowerShell cmdlet '{cmdlet}' is dangerous and prohibited in read-only mode.",
                rawSegment);
        }

        if (!_options.AllowedPowerShellCmdlets.Contains(cmdlet))
        {
            return new CommandValidationResult(
                false,
                $"PowerShell cmdlet '{cmdlet}' is not permitted in read-only mode. Allowed cmdlets: {string.Join(", ", _options.AllowedPowerShellCmdlets)}.",
                rawSegment);
        }

        // Check dangerous parameters
        for (int i = 1; i < tokens.Count; i++)
        {
            var arg = tokens[i];
            if (arg.StartsWith("-OutVariable", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("-ov", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("-PipelineVariable", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("-pv", StringComparison.OrdinalIgnoreCase))
            {
                return new CommandValidationResult(false, $"PowerShell parameter '{arg}' is not permitted in read-only mode.", rawSegment);
            }
        }

        return new CommandValidationResult(true);
    }

    private static string NormalizeBinaryName(string raw)
    {
        var clean = raw.Replace('\\', '/');
        var idx = clean.LastIndexOf('/');
        var name = idx >= 0 ? clean[(idx + 1)..] : clean;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }
        return name;
    }

    private bool TryMatchSafeStderrRedirection(string command, int gtIndex, out int newIndex)
    {
        newIndex = gtIndex;

        if (!_options.AllowStderrRedirection)
        {
            return false;
        }

        // Look backwards from gtIndex to find descriptor '2'
        int descIdx = gtIndex - 1;
        while (descIdx >= 0 && (command[descIdx] == ' ' || command[descIdx] == '\t'))
        {
            descIdx--;
        }

        if (descIdx < 0 || command[descIdx] != '2')
        {
            return false;
        }

        // '2' must be at start of command, preceded by whitespace, or preceded by a command delimiter
        if (descIdx > 0)
        {
            char prev = command[descIdx - 1];
            if (!char.IsWhiteSpace(prev) && prev != ';' && prev != '&' && prev != '|' && prev != '(' && prev != '\n')
            {
                return false;
            }
        }

        // Determine redirection operator: '>' or '>>'
        int afterGt = gtIndex + 1;
        bool isAppend = false;
        if (afterGt < command.Length && command[afterGt] == '>')
        {
            isAppend = true;
            afterGt++;
        }

        // Skip optional spaces after '>' or '>>'
        int targetStart = afterGt;
        while (targetStart < command.Length && (command[targetStart] == ' ' || command[targetStart] == '\t'))
        {
            targetStart++;
        }

        if (targetStart >= command.Length)
        {
            return false;
        }

        // Check for 2>&1 or 2>& 1 (append '>>' is not valid for descriptor duping)
        if (!isAppend && command[targetStart] == '&')
        {
            int afterAmp = targetStart + 1;
            while (afterAmp < command.Length && (command[afterAmp] == ' ' || command[afterAmp] == '\t'))
            {
                afterAmp++;
            }

            if (afterAmp < command.Length && command[afterAmp] == '1')
            {
                // '1' must be followed by end of command, whitespace, or delimiter
                int afterOne = afterAmp + 1;
                if (IsDelimiterOrEnd(command, afterOne))
                {
                    newIndex = afterAmp;
                    return true;
                }
            }

            return false;
        }

        // Check for 2>/dev/null or 2>>/dev/null (Bash)
        if (_options.Dialect == ShellDialect.Bash)
        {
            const string devNull = "/dev/null";
            if (command.AsSpan(targetStart).StartsWith(devNull, StringComparison.Ordinal))
            {
                int afterTarget = targetStart + devNull.Length;
                if (IsDelimiterOrEnd(command, afterTarget))
                {
                    newIndex = afterTarget - 1;
                    return true;
                }
            }

            return false;
        }

        // Check for 2>$null or 2>>$null (PowerShell)
        if (_options.Dialect == ShellDialect.PowerShell)
        {
            const string psNull = "$null";
            if (command.AsSpan(targetStart).StartsWith(psNull, StringComparison.OrdinalIgnoreCase))
            {
                int afterTarget = targetStart + psNull.Length;
                if (IsDelimiterOrEnd(command, afterTarget))
                {
                    newIndex = afterTarget - 1;
                    return true;
                }
            }

            return false;
        }

        return false;
    }

    private static bool IsDelimiterOrEnd(string command, int index)
    {
        if (index >= command.Length)
        {
            return true;
        }

        var c = command[index];
        return char.IsWhiteSpace(c) || c == ';' || c == '&' || c == '|' || c == ')';
    }
}

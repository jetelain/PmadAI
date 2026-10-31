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
/// Validation rule for a specific command or nested subcommand in <see cref="ReadOnlyCommandValidator"/>.
/// </summary>
public class CommandValidationRule
{
    private HashSet<string> _allowedSubcommands = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, CommandValidationRule> _nestedSubcommands = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Subcommands (verbs) permitted for this command or parent subcommand.
    /// When empty and <see cref="NestedSubcommands"/> is empty, positional arguments are not restricted as subcommands.
    /// </summary>
    public HashSet<string> AllowedSubcommands
    {
        get => _allowedSubcommands;
        set => _allowedSubcommands = value.Comparer == StringComparer.OrdinalIgnoreCase
            ? value
            : new HashSet<string>(value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Flags that consume the next token as their argument when scanning for a subcommand verb
    /// (for example, <c>-C</c> or <c>--work-tree</c> for <c>git</c>, or <c>-s</c>, <c>--solution</c>, <c>--format</c> for <c>csnav</c>).
    /// </summary>
    public HashSet<string> FlagsWithArgs { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Flags or options prohibited at this command or subcommand level.
    /// Long flags starting with <c>--</c> automatically match inline <c>--flag=value</c> forms.
    /// Entries ending with <c>*</c> match prefix forms (such as <c>-c*</c> matching <c>-c</c> and <c>-ccore.pager=sh</c>, or <c>-O*</c> matching <c>-Osh</c>).
    /// When subcommands are configured, short flags in <see cref="BlockedFlags"/> are enforced before the subcommand verb while long flags are enforced across the entire command; place subcommand-specific short flags in <see cref="NestedSubcommands"/>.
    /// </summary>
    public HashSet<string> BlockedFlags { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Nested validation rules keyed by subcommand name (for example, <c>branch</c> or <c>grep</c> under <c>git</c>, or <c>server</c> under <c>csnav</c>).
    /// </summary>
    public Dictionary<string, CommandValidationRule> NestedSubcommands
    {
        get => _nestedSubcommands;
        set => _nestedSubcommands = value.Comparer == StringComparer.OrdinalIgnoreCase
            ? value
            : new Dictionary<string, CommandValidationRule>(value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether unconsumed positional arguments are permitted when <see cref="AllowedSubcommands"/> and <see cref="NestedSubcommands"/> are empty.
    /// Set to <c>false</c> for commands where bare positional arguments mutate state (such as <c>git branch &lt;name&gt;</c> or <c>git tag &lt;name&gt;</c>).
    /// Default: <c>true</c>.
    /// </summary>
    public bool AllowPositionalArguments { get; set; } = true;

    /// <summary>
    /// Whether invoking this command or subcommand without a subcommand token is permitted when <see cref="AllowedSubcommands"/> or <see cref="NestedSubcommands"/> is configured
    /// (for example, <c>git remote</c> or <c>git remote -v</c>). When <c>false</c>, only help/version flags (<c>-h</c>, <c>--help</c>, <c>-v</c>, <c>--version</c>) are allowed without a subcommand.
    /// Default: <c>false</c>.
    /// </summary>
    public bool AllowNoSubcommand { get; set; }
}

/// <summary>
/// Configuration options for <see cref="ReadOnlyCommandValidator"/>.
/// </summary>
public class ReadOnlyCommandValidatorOptions
{
    private HashSet<string> _allowedBinaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "find", "rg", "grep", "cat", "ls", "git", "head", "tail", "wc", "cd"
    };

    private HashSet<string> _allowedPowerShellCmdlets = new(StringComparer.OrdinalIgnoreCase)
    {
        "Get-ChildItem", "Get-Content", "Select-String", "Set-Location", "cd"
    };

    private Dictionary<string, CommandValidationRule> _commandRules = CreateDefaultCommandRules();

    /// <summary>Shell dialect being validated. Default: Bash.</summary>
    public ShellDialect Dialect { get; set; } = ShellDialect.Bash;

    /// <summary>Binaries permitted for execution in Bash dialect. Default: read-only utilities.</summary>
    public HashSet<string> AllowedBinaries
    {
        get => _allowedBinaries;
        set => _allowedBinaries = value.Comparer == StringComparer.OrdinalIgnoreCase
            ? value
            : new HashSet<string>(value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Cmdlets permitted for execution in PowerShell dialect. Default: read-only cmdlets (aliases prohibited).</summary>
    public HashSet<string> AllowedPowerShellCmdlets
    {
        get => _allowedPowerShellCmdlets;
        set => _allowedPowerShellCmdlets = value.Comparer == StringComparer.OrdinalIgnoreCase
            ? value
            : new HashSet<string>(value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Per-command subcommand and flag validation rules keyed by binary/cmdlet name.
    /// Pre-configured with default read-only rules for <c>git</c>, <c>find</c>, <c>rg</c>, and <c>sed</c>.
    /// </summary>
    public Dictionary<string, CommandValidationRule> CommandRules
    {
        get => _commandRules;
        set => _commandRules = value.Comparer == StringComparer.OrdinalIgnoreCase
            ? value
            : new Dictionary<string, CommandValidationRule>(value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether to block subshells and substitutions ($(), backticks, process substitutions). Default: true.</summary>
    public bool BlockSubshells { get; set; } = true;

    /// <summary>Whether to block file redirection operators (&gt;, &gt;&gt;, &lt;, etc.). Default: true. Safe stderr redirections remain tolerated unless <see cref="AllowStderrRedirection"/> is false.</summary>
    public bool BlockRedirection { get; set; } = true;

    /// <summary>Whether to tolerate safe standard error redirections (2&gt;/dev/null, 2&gt;&amp;1, and in PowerShell 2&gt;$null) even when BlockRedirection is enabled. Default: true.</summary>
    public bool AllowStderrRedirection { get; set; } = true;

    /// <summary>Whether to enforce that all chained/piped commands resolve to allowed binaries/cmdlets. Default: true.</summary>
    public bool BlockPipesToDisallowedBinaries { get; set; } = true;

    /// <summary>
    /// Creates the default command validation rules for <c>git</c>, <c>find</c>, <c>rg</c>, and <c>sed</c>.
    /// </summary>
    public static Dictionary<string, CommandValidationRule> CreateDefaultCommandRules() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["find"] = new CommandValidationRule
            {
                BlockedFlags = ["-exec", "-execdir", "-ok", "-okdir", "-delete", "-fprint", "-fprint0", "-fprintf", "-fls"]
            },
            ["rg"] = new CommandValidationRule
            {
                BlockedFlags = ["--pre", "--pre-glob", "--hostname-bin"]
            },
            ["sed"] = new CommandValidationRule
            {
                BlockedFlags = ["-i*", "--in-place"]
            },
            ["git"] = new CommandValidationRule
            {
                AllowedSubcommands =
                [
                    "status", "log", "diff", "show", "blame", "ls-files", "grep", "rev-parse",
                    "branch", "tag", "ls-tree", "describe", "shortlog", "remote", "cat-file", "check-ref-format"
                ],
                FlagsWithArgs = ["-C", "--work-tree"],
                BlockedFlags =
                [
                    "-c*", "--config-env", "--exec-path", "--upload-pack", "--paginate", "-p",
                    "--output", "--ext-diff*", "--git-dir", "--namespace", "--super-prefix", "--bare"
                ],
                NestedSubcommands =
                {
                    ["branch"] = new CommandValidationRule
                    {
                        AllowPositionalArguments = false,
                        FlagsWithArgs = ["-l", "--list", "--contains", "--no-contains", "--merged", "--no-merged", "--points-at", "--sort", "--format"],
                        BlockedFlags =
                        [
                            "-d", "-D", "--delete", "-m", "-M", "--move", "-c", "-C", "--copy",
                            "-f", "--force", "-u*", "--set-upstream*", "--unset-upstream", "--edit-description",
                            "-t*", "--track*", "--create-reflog", "--no-create-reflog"
                        ]
                    },
                    ["tag"] = new CommandValidationRule
                    {
                        AllowPositionalArguments = false,
                        FlagsWithArgs = ["-l", "--list", "-v", "--verify", "--contains", "--no-contains", "--merged", "--no-merged", "--points-at", "--sort", "--format"],
                        BlockedFlags =
                        [
                            "-d", "-D", "--delete", "-a", "--annotate", "--no-annotate", "-s", "--sign", "--no-sign",
                            "-f", "--force", "-u*", "--local-user", "-m*", "--message", "-F*", "--file",
                            "-e", "--edit", "--no-edit", "--trailer", "--no-trailer", "--cleanup", "--no-cleanup",
                            "--create-reflog", "--no-create-reflog"
                        ]
                    },
                    ["remote"] = new CommandValidationRule
                    {
                        AllowNoSubcommand = true,
                        AllowedSubcommands = ["show", "get-url"]
                    },
                    ["grep"] = new CommandValidationRule
                    {
                        BlockedFlags = ["-O*", "--open-files-in-pager"]
                    }
                }
            }
        };
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

        // Fast path for single commands without pipes, semicolons, newlines, or boolean chaining
        if (commandLine.IndexOfAny(['|', ';', '&', '\n', '\r']) < 0)
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

                // Check variable expansion and backtick subshells in double quotes or unquoted
                if (inDoubleQuote || !inSingleQuote)
                {
                    if (_options.BlockSubshells && c == '`')
                    {
                        return new CommandValidationResult(false, "Subshell execution ('`') is not permitted in read-only mode.", command);
                    }

                    if (c == '$' && i + 1 < command.Length)
                    {
                        char next = command[i + 1];
                        if (next == '(')
                        {
                            if (_options.BlockSubshells)
                            {
                                return new CommandValidationResult(false, "Subshell execution ('$(') is not permitted in read-only mode.", command);
                            }
                        }
                        else if (!inDoubleQuote && (next == '\'' || next == '"'))
                        {
                            return new CommandValidationResult(false, "Shell dollar-quoting (\"$'...'\" or '$\"...\"') is not permitted in read-only mode.", command);
                        }
                        else if (char.IsLetterOrDigit(next) || next is '_' or '{' or '@' or '*' or '#' or '?' or '-' or '$' or '!')
                        {
                            return new CommandValidationResult(false, "Environment variable expansion ('$' or '${') is not permitted in read-only mode.", command);
                        }
                    }
                }

                // Outside quotes
                if (!inSingleQuote && !inDoubleQuote)
                {
                    if (_options.BlockSubshells)
                    {
                        if ((c == '<' || c == '>') && i + 1 < command.Length && command[i + 1] == '(')
                        {
                            return new CommandValidationResult(false, $"Process substitution ('{c}(') is not permitted in read-only mode.", command);
                        }

                        if (c == '(' || c == ')')
                        {
                            return new CommandValidationResult(false, $"Subshell grouping ('{c}') is not permitted in read-only mode.", command);
                        }
                    }

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
                            if (i + 1 < command.Length && command[i + 1] == '<')
                            {
                                return new CommandValidationResult(false, "Here-doc ('<<') is not permitted in read-only mode.", command);
                            }
                            return new CommandValidationResult(false, "Input redirection ('<') is not permitted in read-only mode.", command);
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
                        else if (char.IsLetterOrDigit(next) || next is '_' or '{' or '?' or '^' or '$' or '`')
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

                    if (_options.BlockSubshells && (c == '(' || c == ')'))
                    {
                        return new CommandValidationResult(false, $"PowerShell parenthetical expression ('{c}') is not permitted in read-only mode.", command);
                    }

                    if (c == '{' || c == '}')
                    {
                        return new CommandValidationResult(false, $"PowerShell script block ('{c}') is not permitted in read-only mode.", command);
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
                else if (c == '\r' || c == '\n')
                {
                    segments.Add(command.Substring(start, i - start));
                    if (c == '\r' && i + 1 < command.Length && command[i + 1] == '\n')
                    {
                        i++;
                    }
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
            int prefixLen = clean.StartsWith("/bin/", StringComparison.Ordinal) ? "/bin/".Length
                : clean.StartsWith("/usr/bin/", StringComparison.Ordinal) ? "/usr/bin/".Length
                : -1;

            if (prefixLen < 0 ||
                clean.IndexOf('/', prefixLen) >= 0 ||
                clean.AsSpan(prefixLen) is "" or "." or "..")
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

        if (_options.CommandRules.TryGetValue(binary, out var rule))
        {
            return ValidateCommandRule(rawSegment, binary, tokens, 1, rule, null);
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

        if (_options.CommandRules.TryGetValue(cmdlet, out var rule))
        {
            return ValidateCommandRule(rawSegment, cmdlet, tokens, 1, rule, null);
        }

        return new CommandValidationResult(true);
    }

    private static CommandValidationResult ValidateCommandRule(
        string rawSegment,
        string commandPath,
        List<string> tokens,
        int startIndex,
        CommandValidationRule rule,
        HashSet<string>? parentFlagsWithArgs)
    {
        bool hasSubcommands = rule.AllowedSubcommands.Count > 0 || rule.NestedSubcommands.Count > 0;
        if (!hasSubcommands)
        {
            for (int j = startIndex; j < tokens.Count; j++)
            {
                var arg = tokens[j];
                if (IsFlagBlocked(arg, rule.BlockedFlags))
                {
                    return new CommandValidationResult(
                        false,
                        $"Flag '{arg}' is not permitted for '{commandPath}' in read-only mode.",
                        rawSegment);
                }

                if (!rule.AllowPositionalArguments)
                {
                    if (arg.Length <= 1 || arg[0] != '-' || arg == "--")
                    {
                        return new CommandValidationResult(
                            false,
                            $"Positional argument '{arg}' is not permitted for '{commandPath}' in read-only mode.",
                            rawSegment);
                    }

                    if (rule.FlagsWithArgs.Contains(arg))
                    {
                        if (j + 1 < tokens.Count)
                        {
                            if (tokens[j + 1][0] == '-')
                            {
                                return new CommandValidationResult(
                                    false,
                                    $"Flag '{arg}' requires a non-flag argument for '{commandPath}' in read-only mode.",
                                    rawSegment);
                            }

                            j++;
                        }
                    }
                }
            }

            return new CommandValidationResult(true);
        }

        int i = startIndex;
        while (i < tokens.Count)
        {
            var token = tokens[i];
            if (token.Length <= 1 || token[0] != '-' || token == "--")
            {
                break;
            }

            if (IsFlagBlocked(token, rule.BlockedFlags))
            {
                return new CommandValidationResult(
                    false,
                    $"Flag '{token}' is not permitted for '{commandPath}' in read-only mode.",
                    rawSegment);
            }

            if (rule.FlagsWithArgs.Contains(token) ||
                (parentFlagsWithArgs != null && parentFlagsWithArgs.Contains(token)))
            {
                if (i + 1 >= tokens.Count || tokens[i + 1][0] == '-')
                {
                    return new CommandValidationResult(
                        false,
                        $"Flag '{token}' requires a non-flag argument for '{commandPath}' in read-only mode.",
                        rawSegment);
                }

                i += 2;
                continue;
            }

            i++;
        }

        if (i >= tokens.Count)
        {
            if (rule.AllowNoSubcommand)
            {
                return new CommandValidationResult(true);
            }

            if (startIndex < tokens.Count && AreAllHelpOrVersionFlags(tokens, startIndex))
            {
                // e.g. "git --version" or "csnav --help"
                return new CommandValidationResult(true);
            }

            var allowedSubcommands = rule.AllowedSubcommands.Count > 0
                ? string.Join(", ", rule.AllowedSubcommands)
                : string.Join(", ", rule.NestedSubcommands.Keys);
            return new CommandValidationResult(
                false,
                $"A subcommand is required for '{commandPath}' in read-only mode. Allowed subcommands: {allowedSubcommands}.",
                rawSegment);
        }

        var subcommand = tokens[i];
        bool isAllowed = rule.AllowedSubcommands.Count > 0
            ? rule.AllowedSubcommands.Contains(subcommand)
            : rule.NestedSubcommands.ContainsKey(subcommand);

        if (!isAllowed)
        {
            var allowedList = rule.AllowedSubcommands.Count > 0
                ? string.Join(", ", rule.AllowedSubcommands)
                : string.Join(", ", rule.NestedSubcommands.Keys);
            return new CommandValidationResult(
                false,
                $"Subcommand '{subcommand}' is not permitted for '{commandPath}' in read-only mode. Allowed subcommands: {allowedList}.",
                rawSegment);
        }

        var subcommandPath = $"{commandPath} {subcommand}";

        // Check command-level blocked long flags across remaining tokens
        if (rule.BlockedFlags.Count > 0)
        {
            for (int j = i + 1; j < tokens.Count; j++)
            {
                var arg = tokens[j];
                if (IsFlagBlocked(arg, rule.BlockedFlags, includeShortFlags: false))
                {
                    return new CommandValidationResult(
                        false,
                        $"Flag '{arg}' is not permitted for '{subcommandPath}' in read-only mode.",
                        rawSegment);
                }
            }
        }

        if (rule.NestedSubcommands.TryGetValue(subcommand, out var nestedRule))
        {
            HashSet<string>? effectiveParentFlags;
            if (rule.FlagsWithArgs.Count > 0 && parentFlagsWithArgs is { Count: > 0 })
            {
                effectiveParentFlags = new HashSet<string>(parentFlagsWithArgs, StringComparer.Ordinal);
                effectiveParentFlags.UnionWith(rule.FlagsWithArgs);
            }
            else
            {
                effectiveParentFlags = rule.FlagsWithArgs.Count > 0 ? rule.FlagsWithArgs : parentFlagsWithArgs;
            }

            return ValidateCommandRule(rawSegment, subcommandPath, tokens, i + 1, nestedRule, effectiveParentFlags);
        }

        return new CommandValidationResult(true);
    }

    private static bool AreAllHelpOrVersionFlags(List<string> tokens, int startIndex)
    {
        for (int i = startIndex; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token is not ("-h" or "--help" or "-v" or "--version"))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFlagBlocked(string token, HashSet<string> blockedFlags, bool includeShortFlags = true)
    {
        if (blockedFlags.Count == 0 || token.Length <= 1 || token[0] != '-')
        {
            return false;
        }

        bool isLongFlag = token.StartsWith("--", StringComparison.Ordinal);
        if (!includeShortFlags && !isLongFlag)
        {
            return false;
        }

        if (blockedFlags.Contains(token))
        {
            return true;
        }

        // Check long flag inline assignment (--flag=value)
        if (isLongFlag)
        {
            int eqIdx = token.IndexOf('=');
            if (eqIdx > 2 && blockedFlags.Contains(token[..eqIdx]))
            {
                return true;
            }
        }

        // Check wildcard prefix patterns (e.g. "-c*", "-O*", "-i*", "--ext-diff*") and bundled short flags
        foreach (var pattern in blockedFlags)
        {
            if (pattern.Length > 1 && pattern[^1] == '*')
            {
                var prefix = pattern.AsSpan(0, pattern.Length - 1);
                bool isShortPrefix = prefix.Length >= 1 && prefix[0] == '-' && (prefix.Length == 1 || prefix[1] != '-');
                if (isShortPrefix)
                {
                    if (isLongFlag)
                    {
                        continue;
                    }

                    // Bundled short flag with optional inline argument (e.g. "-ni", "-Ei.bak", "-nOsh")
                    if (prefix.Length == 2 && char.IsLetterOrDigit(prefix[1]) && token.Length > 2)
                    {
                        int charIdx = token.AsSpan(1).IndexOf(prefix[1]);
                        if (charIdx >= 0 && IsAsciiLetterOrDigitSpan(token.AsSpan(1, charIdx)))
                        {
                            return true;
                        }
                    }
                }

                if (token.AsSpan().StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else if (!isLongFlag && token.Length > 2 && pattern.Length == 2 && pattern[0] == '-' && char.IsLetterOrDigit(pattern[1]))
            {
                // Bundled exact short flag (e.g. "-avD" matching "-D", or "-dlv1.0" matching "-d")
                int charIdx = token.AsSpan(1).IndexOf(pattern[1]);
                if (charIdx >= 0 && IsAsciiLetterOrDigitSpan(token.AsSpan(1, charIdx)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsAsciiLetterOrDigitSpan(ReadOnlySpan<char> span)
    {
        for (int i = 0; i < span.Length; i++)
        {
            if (!char.IsLetterOrDigit(span[i]))
            {
                return false;
            }
        }

        return true;
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

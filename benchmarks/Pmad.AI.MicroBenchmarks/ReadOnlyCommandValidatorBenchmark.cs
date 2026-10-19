using BenchmarkDotNet.Attributes;
using Pmad.AI.Guardrails;

namespace Pmad.AI.MicroBenchmarks;

[Config(typeof(FastInProcessConfig))]
public class ReadOnlyCommandValidatorBenchmark
{
    private ReadOnlyCommandValidator _bashValidator = null!;
    private ReadOnlyCommandValidator _psValidator = null!;

    private const string SimpleBashCmd = "git status";
    private const string FlaggedBashCmd = "rg --line-number --glob \"*.cs\" \"pattern\" src/";
    private const string PipedBashPipeline = "git log --oneline -n 20 | grep \"fix\" | head -n 5";
    private const string RedirectionBashCmd = "echo \"payload\" > /tmp/output.txt";
    private const string DisallowedBinaryBashCmd = "curl -X POST https://example.com/api";

    private const string AllowedPowerShellCmd = "Get-ChildItem -Path . -Filter *.cs -Recurse";
    private const string ProhibitedAliasPowerShellCmd = "gci -Path . -Filter *.cs";
    private const string DangerousCmdletPowerShellCmd = "Invoke-Expression -Command \"Write-Host hello\"";

    [GlobalSetup]
    public void Setup()
    {
        _bashValidator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.Bash
        });

        _psValidator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.PowerShell
        });
    }

    [Benchmark(Baseline = true)]
    public CommandValidationResult Bash_SimpleAllowed() => _bashValidator.ValidateCommand(SimpleBashCmd);

    [Benchmark]
    public CommandValidationResult Bash_FlaggedAllowed() => _bashValidator.ValidateCommand(FlaggedBashCmd);

    [Benchmark]
    public CommandValidationResult Bash_PipedPipeline() => _bashValidator.ValidateCommand(PipedBashPipeline);

    [Benchmark]
    public CommandValidationResult Bash_BlockedRedirection() => _bashValidator.ValidateCommand(RedirectionBashCmd);

    [Benchmark]
    public CommandValidationResult Bash_BlockedBinary() => _bashValidator.ValidateCommand(DisallowedBinaryBashCmd);

    [Benchmark]
    public CommandValidationResult PowerShell_AllowedCmdlet() => _psValidator.ValidateCommand(AllowedPowerShellCmd);

    [Benchmark]
    public CommandValidationResult PowerShell_BlockedAlias() => _psValidator.ValidateCommand(ProhibitedAliasPowerShellCmd);

    [Benchmark]
    public CommandValidationResult PowerShell_BlockedDangerousCmdlet() => _psValidator.ValidateCommand(DangerousCmdletPowerShellCmd);
}

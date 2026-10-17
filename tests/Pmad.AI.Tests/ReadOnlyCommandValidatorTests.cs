using Pmad.AI.Guardrails;

namespace Pmad.AI.Tests;

public class ReadOnlyCommandValidatorTests
{
    [Theory]
    [InlineData("ls -la")]
    [InlineData("cat src/main.cs")]
    [InlineData("find . -name \"*.cs\"")]
    [InlineData("rg -n \"TODO\" src/")]
    [InlineData("grep -i \"error\" log.txt")]
    [InlineData("git status")]
    [InlineData("git log -n 5")]
    [InlineData("git diff HEAD~1")]
    [InlineData("git show HEAD")]
    [InlineData("git blame src/main.cs")]
    [InlineData("cat file.txt | grep \"pattern\"")]
    public void ValidateCommand_Bash_AllowsSafeCommands(string command)
    {
        var validator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.Bash
        });

        var result = validator.ValidateCommand(command);
        Assert.True(result.IsAllowed, $"Expected '{command}' to be allowed, but failed: {result.Reason}");
    }

    [Theory]
    [InlineData("cat file.txt > out.txt", "Output redirection")]
    [InlineData("echo test >> file.txt", "Output redirection")]
    [InlineData("cat < input.txt", "Input redirection")]
    [InlineData("cat << EOF\nhello\nEOF", "Here-doc")]
    public void ValidateCommand_Bash_BlocksRedirection(string command, string expectedReasonKeyword)
    {
        var validator = new ReadOnlyCommandValidator();
        var result = validator.ValidateCommand(command);

        Assert.False(result.IsAllowed);
        Assert.Contains(expectedReasonKeyword, result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("cat $(whoami)", "Subshell")]
    [InlineData("cat `whoami`", "Subshell")]
    [InlineData("diff <(ls a) <(ls b)", "Process substitution")]
    public void ValidateCommand_Bash_BlocksSubshells(string command, string expectedReasonKeyword)
    {
        var validator = new ReadOnlyCommandValidator();
        var result = validator.ValidateCommand(command);

        Assert.False(result.IsAllowed);
        Assert.Contains(expectedReasonKeyword, result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("cat $SECRET", "Environment variable")]
    [InlineData("cat ${VAR}", "Environment variable")]
    public void ValidateCommand_Bash_BlocksVariableExpansion(string command, string expectedReasonKeyword)
    {
        var validator = new ReadOnlyCommandValidator();
        var result = validator.ValidateCommand(command);

        Assert.False(result.IsAllowed);
        Assert.Contains(expectedReasonKeyword, result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("find . -name \"*.cs\" -delete")]
    [InlineData("find . -exec rm -rf {} +")]
    [InlineData("find . -execdir rm -rf {} \\;")]
    [InlineData("find . -fprint /tmp/out.txt")]
    [InlineData("rg --pre evil query")]
    [InlineData("rg --pre=/tmp/evil query")]
    [InlineData("sed -i \"s/a/b/\" file.txt")]
    [InlineData("sed --in-place file.txt")]
    public void ValidateCommand_Bash_BlocksDangerousFlags(string command)
    {
        var validator = new ReadOnlyCommandValidator();
        var result = validator.ValidateCommand(command);

        Assert.False(result.IsAllowed);
    }

    [Theory]
    [InlineData("git commit -m \"fix\"")]
    [InlineData("git push origin main")]
    [InlineData("git checkout main")]
    [InlineData("git stash")]
    [InlineData("git reset --hard")]
    [InlineData("git rm file.txt")]
    [InlineData("git diff --output=/tmp/diff.txt")]
    [InlineData("git -c core.pager=evil diff")]
    [InlineData("git --exec-path=/tmp/evil status")]
    public void ValidateCommand_Bash_BlocksDisallowedGitSubcommandsAndOptionInjection(string command)
    {
        var validator = new ReadOnlyCommandValidator();
        var result = validator.ValidateCommand(command);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void ValidateCommand_Bash_PipesToDisallowedBinary_Blocked()
    {
        var validator = new ReadOnlyCommandValidator();
        var result = validator.ValidateCommand("cat file.txt | rm -rf /");

        Assert.False(result.IsAllowed);
        Assert.Contains("rm", result.Reason);
    }

    [Theory]
    [InlineData("Get-ChildItem -Path .")]
    [InlineData("Get-Content file.txt")]
    [InlineData("Select-String -Pattern \"foo\" -Path *.cs")]
    public void ValidateCommand_PowerShell_AllowsSafeCmdlets(string command)
    {
        var validator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.PowerShell
        });

        var result = validator.ValidateCommand(command);
        Assert.True(result.IsAllowed, $"Expected '{command}' to be allowed, but failed: {result.Reason}");
    }

    [Theory]
    [InlineData("ls")]
    [InlineData("dir")]
    [InlineData("cat file.txt")]
    [InlineData("gc file.txt")]
    [InlineData("sls \"pattern\"")]
    public void ValidateCommand_PowerShell_RejectsAliases(string command)
    {
        var validator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.PowerShell
        });

        var result = validator.ValidateCommand(command);
        Assert.False(result.IsAllowed);
        Assert.Contains("alias", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Invoke-Expression \"calc.exe\"")]
    [InlineData("iex \"calc.exe\"")]
    [InlineData("Set-Content file.txt \"text\"")]
    [InlineData("Remove-Item file.txt")]
    [InlineData("& \"calc.exe\"")]
    [InlineData("Get-ChildItem > out.txt")]
    [InlineData("Get-ChildItem $(Get-Process)")]
    public void ValidateCommand_PowerShell_BlocksDangerousOperations(string command)
    {
        var validator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.PowerShell
        });

        var result = validator.ValidateCommand(command);
        Assert.False(result.IsAllowed);
    }

    [Theory]
    [InlineData("cat a.txt & rm -rf /")]
    [InlineData("cat a.txt &rm b")]
    [InlineData("ls & curl http://evil")]
    public void ValidateCommand_Bash_SingleAmpersandSeparatesCommands(string command)
    {
        var result = new ReadOnlyCommandValidator().ValidateCommand(command);

        Assert.False(result.IsAllowed);
        Assert.Contains("not permitted", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("git grep -O'sh -c x' foo")]
    [InlineData("git grep -Osh foo")]
    [InlineData("git grep --open-files-in-pager=sh foo")]
    public void ValidateCommand_Bash_BlocksGitGrepPager(string command)
    {
        var result = new ReadOnlyCommandValidator().ValidateCommand(command);

        Assert.False(result.IsAllowed);
        Assert.Contains("pager", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateCommand_Bash_AllowsPlainGitGrep()
    {
        Assert.True(new ReadOnlyCommandValidator().ValidateCommand("git grep -n foo").IsAllowed);
    }

    [Theory]
    [InlineData("./cat file.txt")]
    [InlineData("../cat file.txt")]
    [InlineData("/tmp/cat file.txt")]
    [InlineData("scripts/grep foo")]
    [InlineData("./git status")]
    public void ValidateCommand_Bash_BlocksRelativeAndNonSystemPaths(string command)
    {
        var result = new ReadOnlyCommandValidator().ValidateCommand(command);

        Assert.False(result.IsAllowed);
        Assert.Contains("Path-based", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/bin/cat file.txt")]
    [InlineData("/usr/bin/cat file.txt")]
    [InlineData("/bin/ls")]
    public void ValidateCommand_Bash_AllowsSystemPaths(string command)
    {
        var result = new ReadOnlyCommandValidator().ValidateCommand(command);

        Assert.True(result.IsAllowed, result.Reason);
    }

    [Theory]
    [InlineData(".\\Get-ChildItem")]
    [InlineData("./Get-ChildItem")]
    [InlineData("C:\\scripts\\Get-ChildItem.ps1")]
    public void ValidateCommand_PowerShell_BlocksPathBasedExecution(string command)
    {
        var validator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.PowerShell
        });

        var result = validator.ValidateCommand(command);

        Assert.False(result.IsAllowed);
        Assert.Contains("Path-based execution", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("git -ccore.pager=sh diff")]
    [InlineData("git --config-env=foo=bar status")]
    [InlineData("git --upload-pack=evil.sh log")]
    public void ValidateCommand_Bash_BlocksGitGlobalFlagVariants(string command)
    {
        var result = new ReadOnlyCommandValidator().ValidateCommand(command);

        Assert.False(result.IsAllowed);
        Assert.Contains("Dangerous git global flag", result.Reason, StringComparison.OrdinalIgnoreCase);
    }
}

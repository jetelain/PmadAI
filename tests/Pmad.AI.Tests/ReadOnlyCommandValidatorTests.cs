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
    [InlineData("git branch")]
    [InlineData("git branch -a")]
    [InlineData("git branch --show-current")]
    [InlineData("git branch -l \"main*\"")]
    [InlineData("git branch --contains HEAD")]
    [InlineData("git tag")]
    [InlineData("git tag -l \"v1.*\"")]
    [InlineData("git tag -v v1.0.0")]
    [InlineData("git tag --points-at HEAD")]
    [InlineData("git ls-tree HEAD")]
    [InlineData("git describe --tags")]
    [InlineData("git shortlog -sn")]
    [InlineData("git shortlog -c")]
    [InlineData("git remote")]
    [InlineData("git remote -v")]
    [InlineData("git remote show origin")]
    [InlineData("git remote get-url origin")]
    [InlineData("git cat-file -p HEAD")]
    [InlineData("git check-ref-format refs/heads/main")]
    [InlineData("git grep -c \"pattern\"")]
    [InlineData("git ls-files -c")]
    [InlineData("git log -c -n 1")]
    [InlineData("git show -c HEAD")]
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
    [InlineData("git branch -d old-branch")]
    [InlineData("git branch -D old-branch")]
    [InlineData("git branch -avD old-branch")]
    [InlineData("git branch --delete old-branch")]
    [InlineData("git branch -m old-branch new-branch")]
    [InlineData("git branch -M old-branch new-branch")]
    [InlineData("git branch -c old-branch new-branch")]
    [InlineData("git branch -C old-branch new-branch")]
    [InlineData("git branch -f main HEAD~1")]
    [InlineData("git branch --force main HEAD~1")]
    [InlineData("git branch -u origin/main")]
    [InlineData("git branch --set-upstream-to=origin/main")]
    [InlineData("git branch --unset-upstream")]
    [InlineData("git branch --edit-description")]
    [InlineData("git branch new-branch")]
    [InlineData("git branch -a new-branch")]
    [InlineData("git branch -- new-branch")]
    [InlineData("git tag -d v1.0.0")]
    [InlineData("git tag -D v1.0.0")]
    [InlineData("git tag --delete v1.0.0")]
    [InlineData("git tag v1.0.0")]
    [InlineData("git tag -a v1.0.0 -m \"release\"")]
    [InlineData("git tag -f v1.0.0")]
    [InlineData("git tag -s v1.0.0")]
    [InlineData("git remote add origin https://example.com/repo.git")]
    [InlineData("git remote remove origin")]
    [InlineData("git remote rm origin")]
    [InlineData("git remote set-url origin https://example.com/repo.git")]
    [InlineData("git remote rename origin upstream")]
    [InlineData("git remote prune origin")]
    [InlineData("git remote -v add origin https://example.com/repo.git")]
    [InlineData("git -C status commit -m \"smuggled\"")]
    [InlineData("git --git-dir status commit -m \"smuggled\"")]
    [InlineData("git --git-dir=/tmp/evil status")]
    [InlineData("git --namespace status commit -m \"smuggled\"")]
    [InlineData("git --super-prefix status commit -m \"smuggled\"")]
    [InlineData("git --bare status")]
    [InlineData("git -p status")]
    [InlineData("git -C /tmp")]
    [InlineData("git -- commit -m \"smuggled\"")]
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
    [InlineData("git grep -nOsh foo")]
    [InlineData("git grep --open-files-in-pager=sh foo")]
    public void ValidateCommand_Bash_BlocksGitGrepPager(string command)
    {
        var result = new ReadOnlyCommandValidator().ValidateCommand(command);

        Assert.False(result.IsAllowed);
        Assert.Contains("git grep", result.Reason, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("not permitted for 'git'", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("cd src")]
    [InlineData("cd ..")]
    [InlineData("cd /repo && git status")]
    [InlineData("cd src; ls -la")]
    [InlineData("cd -")]
    [InlineData("cd --")]
    [InlineData("cd /tmp && find . -name \"*.cs\"")]
    public void ValidateCommand_Bash_AllowsCdOutOfTheBox(string command)
    {
        var validator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.Bash
        });

        var result = validator.ValidateCommand(command);
        Assert.True(result.IsAllowed, $"Expected '{command}' to be allowed, but failed: {result.Reason}");
    }

    [Theory]
    [InlineData("cd src")]
    [InlineData("cd ..")]
    [InlineData("Set-Location src")]
    [InlineData("Set-Location ..")]
    public void ValidateCommand_PowerShell_AllowsCdAndSetLocation(string command)
    {
        var validator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.PowerShell
        });

        var result = validator.ValidateCommand(command);
        Assert.True(result.IsAllowed, $"Expected '{command}' to be allowed, but failed: {result.Reason}");
    }

    [Theory]
    [InlineData("find . -name \"*.cs\" 2>/dev/null")]
    [InlineData("find . -name \"*.cs\" 2> /dev/null")]
    [InlineData("find . -name \"*.cs\" 2>>/dev/null")]
    [InlineData("find . -name \"*.cs\" 2>> /dev/null")]
    [InlineData("grep -i \"error\" log.txt 2>/dev/null")]
    [InlineData("git status 2>&1")]
    [InlineData("git status 2>& 1")]
    [InlineData("git status 2 >&1")]
    [InlineData("git diff 2>&1 | grep \"TODO\"")]
    [InlineData("cd src && git status 2>&1")]
    [InlineData("cat file.txt 2>/dev/null | wc -l")]
    public void ValidateCommand_Bash_ToleratesSafeStderrRedirections(string command)
    {
        var validator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.Bash
        });

        var result = validator.ValidateCommand(command);
        Assert.True(result.IsAllowed, $"Expected '{command}' to be allowed, but failed: {result.Reason}");
    }

    [Theory]
    [InlineData("Get-ChildItem -Path . 2>&1")]
    [InlineData("Get-ChildItem -Path . 2>$null")]
    [InlineData("Get-ChildItem -Path . 2> $null")]
    [InlineData("Get-ChildItem -Path . 2>>$null")]
    [InlineData("Get-Content file.txt 2>&1")]
    public void ValidateCommand_PowerShell_ToleratesSafeStderrRedirections(string command)
    {
        var validator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.PowerShell
        });

        var result = validator.ValidateCommand(command);
        Assert.True(result.IsAllowed, $"Expected '{command}' to be allowed, but failed: {result.Reason}");
    }

    [Theory]
    [InlineData("cat secret.txt > out.txt 2>&1", "Output redirection")]
    [InlineData("cat secret.txt 2> out.txt", "Output redirection")]
    [InlineData("cat secret.txt 2>> out.txt", "Output redirection")]
    [InlineData("git status 2>&1 > file.txt", "Output redirection")]
    [InlineData("cat file2>/dev/null", "Output redirection")]
    [InlineData("cat file.txt 2>/dev/null/evil", "Output redirection")]
    [InlineData("cat file.txt 2>&10", "Output redirection")]
    [InlineData("cat file.txt 1>/dev/null", "Output redirection")]
    [InlineData("cat file.txt &> /dev/null", "Output redirection")]
    [InlineData("cat file.txt 2>1.txt", "Output redirection")]
    [InlineData("cat file.txt 2>&1.txt", "Output redirection")]
    public void ValidateCommand_Bash_StillBlocksArbitraryRedirections(string command, string expectedReasonKeyword)
    {
        var validator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.Bash
        });

        var result = validator.ValidateCommand(command);
        Assert.False(result.IsAllowed, $"Expected '{command}' to be blocked, but was allowed.");
        Assert.Contains(expectedReasonKeyword, result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Get-ChildItem 2>/dev/null")]
    [InlineData("Get-ChildItem 2> file.txt")]
    [InlineData("Get-ChildItem 2>> file.txt")]
    public void ValidateCommand_PowerShell_BlocksNonSafeRedirections(string command)
    {
        var validator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.PowerShell
        });

        var result = validator.ValidateCommand(command);
        Assert.False(result.IsAllowed, $"Expected '{command}' to be blocked in PowerShell, but was allowed.");
    }

    [Fact]
    public void ValidateCommand_AllowStderrRedirection_CanBeDisabled()
    {
        var validator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.Bash,
            AllowStderrRedirection = false
        });

        var resNull = validator.ValidateCommand("git status 2>/dev/null");
        Assert.False(resNull.IsAllowed);

        var resMerge = validator.ValidateCommand("git status 2>&1");
        Assert.False(resMerge.IsAllowed);
    }

    [Theory]
    [InlineData("ls 2>&1 & rm x")]
    [InlineData("ls 2>&1&rm x")]
    [InlineData("ls 2>/dev/null &rm x")]
    [InlineData("ls & rm x")]
    public void ValidateCommand_Bash_StillSplitsOnBackgroundAmpersand(string command)
    {
        var validator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.Bash
        });

        var result = validator.ValidateCommand(command);
        Assert.False(result.IsAllowed, $"Expected '{command}' to be blocked, but was allowed.");
    }

    [Fact]
    public void ValidateCommand_AllowStderrRedirection_Disabled_WithBlockRedirectionDisabled_DoesNotThrow()
    {
        var validator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.Bash,
            AllowStderrRedirection = false,
            BlockRedirection = false
        });

        var result = validator.ValidateCommand("git status 2>&1");
        Assert.True(result.IsAllowed, result.Reason);
    }

    [Theory]
    [InlineData("Get-ChildItem -Path . 2>&1 | Select-String foo")]
    [InlineData("Get-Content file.txt 2>$null | Select-String foo")]
    public void ValidateCommand_PowerShell_AllowsStderrRedirectionInPipeline(string command)
    {
        var validator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.PowerShell
        });

        var result = validator.ValidateCommand(command);
        Assert.True(result.IsAllowed, $"Expected '{command}' to be allowed, but failed: {result.Reason}");
    }

    [Theory]
    [InlineData("csnav definition -t CrucibleAgentHarness")]
    [InlineData("csnav explore -t IdeationService")]
    [InlineData("csnav references -t IWorkspaceSymbolService")]
    [InlineData("csnav api-search -p \"*Symbol*\"")]
    [InlineData("csnav server status")]
    [InlineData("csnav server start -s NightCodeForge.sln")]
    [InlineData("csnav -s NightCodeForge.sln server reload")]
    [InlineData("csnav server --format json status")]
    [InlineData("csnav --help")]
    public void ValidateCommand_CustomCommandRules_AllowsConfiguredSubcommandsAndNestedVerbs(string command)
    {
        var validator = CreateCsNavValidator();

        var result = validator.ValidateCommand(command);
        Assert.True(result.IsAllowed, $"Expected '{command}' to be allowed, but failed: {result.Reason}");
    }

    [Theory]
    [InlineData("csnav skills install", "csnav")]
    [InlineData("csnav server stop", "csnav server")]
    [InlineData("csnav -s status skills install", "csnav")]
    [InlineData("csnav server --format json stop", "csnav server")]
    public void ValidateCommand_CustomCommandRules_BlocksDisallowedSubcommandsAndNestedVerbs(string command, string expectedCommandPath)
    {
        var validator = CreateCsNavValidator();

        var result = validator.ValidateCommand(command);
        Assert.False(result.IsAllowed, $"Expected '{command}' to be blocked, but was allowed.");
        Assert.Contains($"not permitted for '{expectedCommandPath}'", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("csnav")]
    [InlineData("csnav -s NightCodeForge.sln")]
    [InlineData("csnav --unknown-flag")]
    [InlineData("csnav server -s NightCodeForge.sln")]
    public void ValidateCommand_CustomCommandRules_BlocksMissingSubcommandWhenNotHelpOrVersion(string command)
    {
        var validator = CreateCsNavValidator();

        var result = validator.ValidateCommand(command);
        Assert.False(result.IsAllowed, $"Expected '{command}' to be blocked, but was allowed.");
        Assert.Contains("subcommand is required", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("sed -n \"1,20p\" file.txt", true)]
    [InlineData("sed -E \"s/foo/bar/g\" file.txt", true)]
    [InlineData("sed -i \"s/foo/bar/g\" file.txt", false)]
    [InlineData("sed -i.bak \"s/foo/bar/g\" file.txt", false)]
    [InlineData("sed -ni \"s/foo/bar/g\" file.txt", false)]
    [InlineData("sed -Ei.bak \"s/foo/bar/g\" file.txt", false)]
    [InlineData("sed --in-place \"s/foo/bar/g\" file.txt", false)]
    [InlineData("sed --in-place=.bak \"s/foo/bar/g\" file.txt", false)]
    public void ValidateCommand_SedAllowed_EnforcesDefaultInPlaceBlocksIncludingBundledFlags(string command, bool expectedAllowed)
    {
        var options = new ReadOnlyCommandValidatorOptions();
        options.AllowedBinaries.Add("sed");
        var validator = new ReadOnlyCommandValidator(options);

        var result = validator.ValidateCommand(command);
        Assert.Equal(expectedAllowed, result.IsAllowed);
    }

    [Fact]
    public void ValidateCommand_MultiLevelNestedSubcommands_MergesParentFlagsWithArgs()
    {
        var options = new ReadOnlyCommandValidatorOptions();
        options.AllowedBinaries.Add("mycli");
        options.CommandRules["mycli"] = new CommandValidationRule
        {
            FlagsWithArgs = ["--env"],
            AllowedSubcommands = ["cloud"],
            NestedSubcommands =
            {
                ["cloud"] = new CommandValidationRule
                {
                    FlagsWithArgs = ["--region"],
                    AllowedSubcommands = ["cluster"],
                    NestedSubcommands =
                    {
                        ["cluster"] = new CommandValidationRule
                        {
                            AllowedSubcommands = ["list", "describe"]
                        }
                    }
                }
            }
        };

        var validator = new ReadOnlyCommandValidator(options);

        // Both --env (level 1) and --region (level 2) should be recognized as taking arguments at level 3
        Assert.True(validator.ValidateCommand("mycli cloud cluster --env prod --region eu-west-1 list").IsAllowed);
        Assert.False(validator.ValidateCommand("mycli cloud cluster --env list --region describe delete").IsAllowed);
    }

    private static ReadOnlyCommandValidator CreateCsNavValidator()
    {
        var options = new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.Bash
        };
        options.AllowedBinaries.Add("csnav");
        options.CommandRules["csnav"] = new CommandValidationRule
        {
            AllowedSubcommands =
            [
                "references", "refs",
                "definition", "def",
                "implementations", "impl",
                "api-search", "apis",
                "explore",
                "decompile",
                "sync",
                "server"
            ],
            FlagsWithArgs = ["-s", "--solution", "--format"],
            NestedSubcommands =
            {
                ["server"] = new CommandValidationRule
                {
                    AllowedSubcommands = ["status", "start", "reload"]
                }
            }
        };

        return new ReadOnlyCommandValidator(options);
    }
}

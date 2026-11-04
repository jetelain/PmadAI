# Pmad.AI.DiffGuard (`diffguard`)

[![NuGet](https://img.shields.io/nuget/v/Pmad.AI.DiffGuard.svg)](https://www.nuget.org/packages/Pmad.AI.DiffGuard)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](../../LICENSE)

`diffguard` is a high-performance Git diff security guardrail and supply-chain screener designed for local developer workflows and automated CI/CD pipelines. It combines zero-latency deterministic static analyzers with an LLM security judge to detect vulnerabilities, backdoors, credential leaks, and evasive payloads before code is merged.

---

## Key Capabilities

- **Deterministic Static Analyzers**:
  - **Payload & Secret Scanning (`PAY-*`)**: High Shannon entropy detection, cloud API tokens, private keys, Base64/hex payload decoders, unicode bidi overrides, homoglyph confusables, and excessive line lengths.
  - **Network & Backdoors (`NET-*`, `BD-*`)**: Reverse shells, raw socket connections, suspicious non-standard ports, external IP addresses, exfiltration endpoints, authentication bypasses, dynamic code invocation, and build/hook tampering.
  - **Dependency & Supply Chain (`DEP-*`)**: New package additions, version downgrades, and untrusted package sources across `.csproj`, `Directory.Packages.props`, `package.json`, and `requirements.txt`.
  - **Binary & Image Inspection (`PAY-BIN-*`)**: Uncommitted binaries, magic byte validation for images (`.png`, `.jpg`, `.webp`, `.ico`), and polyglot executable detection.
  - **Blast Radius & Scope (`SCOPE-*`)**: Mass-file modifications, excessive line diffs, and whole-repository impact anomalies.
  - **Prompt Injection (`PI001`–`PI013`)**: Directive overrides, persona hijacks, control tokens, hidden comment injections, and output format coercion targeting AI code reviewers.
- **Test Tolerance**:
  - Context-aware filtering allows single-label mock hosts (`desktop:11434`), well-known test assertion packages (`xunit`, `NUnit`, `Moq`, `AwesomeAssertions`, etc.), and mock fixture keys without flagging false positives in test suites.
- **LLM Security Judge**:
  - Evaluates semantic diffs against backdoor patterns and prompt injections using local LLMs via **Ollama** or cloud models via **OpenAI / OpenAI-compatible** endpoints.
  - Streaming diff chunking with deleted-line compression.
- **Standardized Reporting**:
  - Structured console output with severity summaries and code snippets.
  - GitHub-Flavored Markdown summary reports.
  - **SARIF 2.1.0** reports for seamless integration with GitHub Code Scanning, Azure DevOps, and GitLab Security.

---

## Installation

### .NET Global Tool

```bash
dotnet tool install -g Pmad.AI.DiffGuard
```

### Build from Source

```bash
dotnet build src/Pmad.AI.DiffGuard.Cli -c Release
```

---

## Quickstart

### Scan Staged Changes (Pre-commit)
```bash
diffguard --staged
```

### Scan Working Tree (Uncommitted changes)
```bash
diffguard --working-tree
```

### Scan a Specific Commit
```bash
diffguard --commit HEAD~1
```

### Scan a Pull Request Branch Range
```bash
diffguard origin/main HEAD
```

### Run with Local Ollama LLM Judge
```bash
diffguard origin/main HEAD --ollama-model qwen2.5-coder:7b
```

### Run with OpenAI LLM Judge
```bash
export DIFFGUARD_API_KEY="sk-..."
diffguard origin/main HEAD --openai-model gpt-4.1-mini
```

---

## GitHub Actions Integration (Non-Blocking SARIF)

You can run `diffguard` as an **advisory (non-blocking)** security check on Pull Requests. In this mode:
- `diffguard` runs deterministic scanners and the LLM judge.
- Findings are exported as a **SARIF 2.1.0** report.
- `continue-on-error: true` ensures the step does not fail the build if risks are detected.
- The SARIF file is uploaded to **GitHub Code Scanning**, annotating the Pull Request diff and populating the repository's **Security &rarr; Code scanning alerts** tab.

### Example Workflow: `.github/workflows/diffguard.yml`

```yaml
name: DiffGuard Security Screener

on:
  pull_request:
    branches: [ main ]

jobs:
  diffguard:
    name: Git Diff Security Analysis
    runs-on: ubuntu-latest
    permissions:
      contents: read
      security-events: write # Required for upload-sarif

    steps:
      - name: Checkout repository
        uses: actions/checkout@v4
        with:
          # Fetch full history so merge-base and comparison commits are accessible
          fetch-depth: 0

      - name: Setup .NET SDK
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Install DiffGuard CLI
        run: |
          dotnet tool install -g Pmad.AI.DiffGuard

      - name: Run DiffGuard (Non-blocking)
        id: diffguard
        continue-on-error: true # Non-blocking advisory mode: workflow continues on findings
        env:
          DIFFGUARD_API_KEY: ${{ secrets.DIFFGUARD_API_KEY }} # Secret API key for OpenAI
        run: |
          diffguard \
            --base origin/${{ github.base_ref }} \
            --head ${{ github.event.pull_request.head.sha }} \
            --sarif diffguard-results.sarif \
            --openai-model gpt-4.1-mini \
            --min-severity Medium

      - name: Upload SARIF to GitHub Code Scanning
        uses: github/codeql-action/upload-sarif@v3
        if: always() # Upload results even if diffguard detected risks
        with:
          sarif_file: diffguard-results.sarif
          category: diffguard
```

> **Note on API Keys**: You can configure your OpenAI API key as a repository secret under **Settings &rarr; Secrets and variables &rarr; Actions** as `DIFFGUARD_API_KEY` (or `OPENAI_API_KEY`).

---

## Configuration (`.diffguard.json`)

`diffguard` resolves configuration across a 3-tier hierarchy (**System &gt; User &gt; Repository**), where System configuration can lock security policies (`lockPolicy: true`) and disallow lower-tier allowlist additions (`allowLowerTierAllowlistAdditions: false`).

At the repository level, `diffguard` automatically discovers `.diffguard.json`, `diffguard.json`, `.config/diffguard.json`, or `.github/diffguard.json` (or via `--config <path>` / `DIFFGUARD_CONFIG`). Any diff that adds, modifies, renames, or deletes a repository DiffGuard configuration file is unconditionally flagged as **`Critical` (`BD-CONFIG-001`)** to enforce mandatory human review.

```json
{
  "minimumRiskSeverity": "Medium",
  "scanners": {
    "enableDeterministic": true,
    "enableNetworkEndpointRules": true,
    "enableBackdoorSignatureRules": true,
    "enableSecretAndEntropyRules": true,
    "enableEncodedPayloadRules": true,
    "enableUnicodeObfuscationRules": true,
    "enableBinaryFileRules": true,
    "enableDependencyRules": true,
    "enableBlastRadiusRules": true,
    "enablePromptInjectionRules": false,
    "flagPrivateNetworkIps": false,
    "enableTestTolerance": true
  },
  "thresholds": {
    "maxChangedFiles": 50,
    "maxChangedLines": 3000,
    "maxCodebaseImpactPercent": 10.0,
    "maxFileImpactPercent": 15.0,
    "shannonEntropyThreshold": 4.95,
    "entropyMinStringLength": 20,
    "minSuspiciousBase64Length": 64,
    "minSuspiciousHexLength": 64,
    "maxLineLength": 500
  },
  "allowlists": {
    "replaceDefaults": false,
    "wellKnownSafeDomains": [ "example.com", "*.local", "internal.corp" ],
    "allowedIpAddresses": [ "127.0.0.1" ],
    "wellKnownSafePackages": [ "Microsoft.", "System.", "xunit" ],
    "excludedFileGlobs": [ "**/*.Designer.cs", "**/package-lock.json" ]
  },
  "llmJudge": {
    "enabled": true,
    "provider": "openai",
    "model": "gpt-4.1-mini",
    "maxChunkBytes": 120000,
    "filterDeletedLines": false,
    "failClosedOnError": true
  }
}
```

---

## CLI Options Reference

| Option | Shorthand | Description | Default |
|---|---|---|---|
| `--staged`, `--cached` | | Scan changes in Git index (default when no target specified) | `true` |
| `--working-tree` | | Scan uncommitted changes in working tree against `HEAD` | `false` |
| `--commit <sha>` | | Scan the diff introduced by a single commit | |
| `--diff-file <path>` | | Scan a pre-generated unified `.diff` or `.patch` file | |
| `--base <ref>` | | Base branch/commit (positional `base` or `base...head` also supported) | |
| `--head <ref>` | | Target branch/commit (positional `head` also supported) | `HEAD` |
| `--two-dot` | | Use direct tip diff (`base..head`) instead of merge-base (`base...head`) | `false` |
| `--repo <path>` | `-r` | Path to the target Git repository root or subdirectory | `.` |
| `--config <path>` | `-c` | Explicit path to a configuration JSON file | |
| `--no-baseline` | | Skip computing repository baseline file/line counts | `false` |
| `--format <fmt>` | `-f` | Primary format: `console`, `markdown` (`md`), or `sarif` | `console` |
| `--output <file>` | `-o` | Write primary format report to file instead of stdout | |
| `--sarif <file>` | | Write a SARIF 2.1.0 report to the specified file | |
| `--markdown <file>`| `--md` | Write a Markdown report to the specified file | |
| `--min-severity <lvl>`| | Threshold for exit code 2: `Low`, `Medium`, `High`, `Critical` | `Medium` |
| `--quiet` | `-q` | Suppress console report output on stdout | `false` |
| `--llm-provider <p>` | | LLM provider: `none`, `ollama`, or `openai` | `none` |
| `--llm-model <id>` | `-m` | Model identifier for the LLM security judge | |
| `--ollama-model <id>`| | Shorthand for `--llm-provider ollama --llm-model <id>` | |
| `--openai-model <id>`| | Shorthand for `--llm-provider openai --llm-model <id>` | |
| `--llm-endpoint <url>`| | Custom API endpoint for LLM provider | |
| `--llm-api-key <key>`| | API key (or use `DIFFGUARD_API_KEY` / `OPENAI_API_KEY`) | |
| `--no-llm` | | Explicitly disable LLM judge | `false` |
| `--filter-deleted-lines` | | Compress contiguous blocks of deleted lines in LLM chunks | `false` |
| `--max-chunk-bytes <n>` | | Maximum UTF-8 bytes per diff chunk sent to LLM judge | `120000` |
| `--fail-open-on-llm-error` | | Do not emit `LLM-JUDGE-ERROR` if the LLM judge request fails | `false` |
| `--no-deterministic` | | Disable deterministic scanners and run only the LLM judge | `false` |
| `--enable-prompt-injection` | | Enable prompt-injection detection rules (`PI001`–`PI013`) | `false` |
| `--no-prompt-injection` | | Disable prompt-injection detection rules | `false` |
| `--flag-private-ips` | | Flag private network IP literals (`NET-IP-001`) | `false` |
| `--allow-domain <d>` | | Additional trusted domain(s) appended to safe domains | |
| `--allow-ip <ip>` | | Additional trusted IP address(es) appended to allowed IPs | |
| `--allow-package <p>` | | Additional trusted package prefix(es) appended to safe packages | |
| `--exclude <glob>` | | Additional file glob pattern(s) appended to excluded files | |
| `--max-changed-files <n>` | | Maximum changed files threshold for blast radius | `50` |
| `--max-changed-lines <n>` | | Maximum changed lines threshold for blast radius | `3000` |
| `--max-codebase-pct <n>` | | Maximum codebase impact percentage for blast radius | `10.0` |
| `--max-file-pct <n>` | | Maximum file impact percentage for blast radius | `15.0` |

---

## Exit Codes

| Exit Code | Meaning |
|---|---|
| `0` | Success &mdash; No findings at or above `MinimumRiskSeverity`. |
| `1` | Error &mdash; Configuration error, invalid CLI arguments, or Git execution failure. |
| `2` | Risks Detected &mdash; One or more findings met or exceeded `MinimumRiskSeverity`. |
| `130` | Canceled &mdash; Execution interrupted by user (`Ctrl+C`). |


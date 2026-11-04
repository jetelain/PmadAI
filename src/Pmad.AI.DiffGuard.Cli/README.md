# Pmad.AI.DiffGuard (`diffguard`)

[![NuGet](https://img.shields.io/nuget/v/Pmad.AI.DiffGuard.svg)](https://www.nuget.org/packages/Pmad.AI.DiffGuard)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](../../LICENSE)

`diffguard` is a high-performance Git diff security guardrail and supply-chain screener designed for local developer workflows and automated CI/CD pipelines. It combines zero-latency deterministic static analyzers with an LLM security judge to detect vulnerabilities, backdoors, credential leaks, and evasive payloads before code is merged.

---

## Key Capabilities

- **Deterministic Static Analyzers**:
  - **Payload & Secret Scanning (`PAY-*`)**: High Shannon entropy detection, cloud API tokens, private keys, Base64/hex payload decoders, unicode bidi overrides, homoglyph confusables, and excessive line lengths.
  - **Network & Backdoors (`NET-*`)**: Reverse shells, raw socket connections, suspicious non-standard ports, external IP addresses, exfiltration endpoints, and dynamic code invocation.
  - **Dependency & Supply Chain (`DEP-*`)**: New package additions and manifest manipulations across `.csproj`, `Directory.Packages.props`, `package.json`, and `requirements.txt`.
  - **Binary & Image Inspection (`BIN-*`)**: Uncommitted binaries, magic byte validation for images (`.png`, `.jpg`, `.webp`, `.ico`), and polyglot executable detection.
  - **Blast Radius & Scope (`SCOPE-*`)**: Mass-file modifications, excessive line diffs, and whole-repository impact anomalies.
- **Test Tolerance**:
  - Context-aware filtering allows single-label mock hosts (`desktop:11434`), well-known test assertion packages (`xunit`, `NUnit`, `Moq`, `AwesomeAssertions`, etc.), and mock fixture keys without flagging false positives in test suites.
- **LLM Security Judge**:
  - Evaluates semantic diffs against backdoor patterns and prompt injections using local LLMs via **Ollama** or cloud models via **OpenAI / OpenAI-compatible** endpoints.
  - Streaming diff chunking with deleted-line compression.
- **Standardized Reporting**:
  - Interactive ANSI console output with colorized findings and code snippets.
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

## Configuration (`diffguard.json`)

`diffguard` automatically discovers repository configuration in `.diffguard.json`, `diffguard.json`, or `.github/diffguard.json`.

```json
{
  "scanners": {
    "network": true,
    "backdoor": true,
    "secrets": true,
    "payloads": true,
    "unicode": true,
    "binary": true,
    "dependencies": true,
    "blastRadius": true,
    "llm": true,
    "promptInjection": false
  },
  "tolerances": {
    "testTolerance": true,
    "safeDomains": [ "example.com", "*.local", "internal.corp" ],
    "safePackages": [ "Microsoft.*", "System.*", "xunit" ]
  },
  "thresholds": {
    "minimumRiskSeverity": "Medium",
    "shannonEntropy": 4.95,
    "maxChangedFiles": 50,
    "maxChangedLines": 3000
  },
  "llm": {
    "provider": "openai",
    "model": "gpt-4.1-mini",
    "failClosedOnError": true
  }
}
```

---

## CLI Options Reference

| Option | Shorthand | Description | Default |
|---|---|---|---|
| `--staged`, `--cached` | | Scan changes in Git index | `false` |
| `--working-tree` | | Scan uncommitted changes in working tree | `false` |
| `--commit <sha>` | | Scan a single commit | |
| `--base <ref>` | | Base branch/commit (positional `base` also supported) | |
| `--head <ref>` | | Target branch/commit (positional `head` also supported) | `HEAD` |
| `--format <fmt>` | `-f` | Primary format: `console`, `markdown`, or `sarif` | `console` |
| `--sarif <file>` | | Output a SARIF 2.1.0 file | |
| `--markdown <file>`| `--md` | Output a Markdown report file | |
| `--min-severity <lvl>`| | Threshold for exit code 2: `Low`, `Medium`, `High`, `Critical` | `Medium` |
| `--ollama-model <id>`| | Use Ollama with specified model | |
| `--openai-model <id>`| | Use OpenAI with specified model | |
| `--llm-endpoint <url>`| | Custom API endpoint for LLM provider | |
| `--llm-api-key <key>`| | API key (or use `DIFFGUARD_API_KEY` / `OPENAI_API_KEY`) | |
| `--no-llm` | | Explicitly disable LLM judge | `false` |
| `--config <path>` | `-c` | Path to custom configuration file | |

---

## Exit Codes

| Exit Code | Meaning |
|---|---|
| `0` | Success &mdash; No findings at or above `MinimumRiskSeverity`. |
| `1` | Error &mdash; Configuration error, invalid CLI arguments, or Git execution failure. |
| `2` | Risks Detected &mdash; One or more findings met or exceeded `MinimumRiskSeverity`. |
| `130` | Canceled &mdash; Execution interrupted by user (`Ctrl+C`). |

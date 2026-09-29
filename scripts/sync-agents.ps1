<#
Regenerates the per-vendor agent config files from the single canonical
source under docs/agents/*.md.

docs/agents/<name>.md is the only file that should ever be hand-edited.
Everything below is derived from it:
  - .claude/agents/<name>.md  (verbatim copy — Claude Code reads this format natively)
  - .cursor/agents/<name>.md  (verbatim copy — Cursor reads the same format natively)
  - .codex/agents/<name>.toml (generated wrapper — Codex needs TOML, not Markdown)
  - .codex/config.toml        (agent table regenerated from the same name/description)

Run after editing any file in docs/agents/.
#>

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$AgentsSrcDir = Join-Path $RepoRoot "docs/agents"
$ClaudeDir = Join-Path $RepoRoot ".claude/agents"
$CursorDir = Join-Path $RepoRoot ".cursor/agents"
$CodexAgentsDir = Join-Path $RepoRoot ".codex/agents"
$CodexConfigPath = Join-Path $RepoRoot ".codex/config.toml"

# Fixed order so .codex/config.toml stays stable and readable across runs.
$AgentOrder = @("orchestrator", "architect", "backend", "frontend", "database", "ui-ux", "tester", "security", "reviewer")

New-Item -ItemType Directory -Force -Path $ClaudeDir | Out-Null
New-Item -ItemType Directory -Force -Path $CursorDir | Out-Null
New-Item -ItemType Directory -Force -Path $CodexAgentsDir | Out-Null

$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-TextFile([string]$Path, [string]$Content) {
    [System.IO.File]::WriteAllText($Path, $Content, $Utf8NoBom)
}

$configEntries = @()

foreach ($agentName in $AgentOrder) {
    $srcPath = Join-Path $AgentsSrcDir "$agentName.md"
    if (-not (Test-Path $srcPath)) {
        throw "Missing canonical agent source: $srcPath"
    }

    $raw = Get-Content -Path $srcPath -Raw -Encoding utf8

    if ($raw -notmatch '(?s)^---\r?\n(?<fm>.*?)\r?\n---\r?\n(?<body>.*)$') {
        throw "Could not parse YAML frontmatter in $srcPath"
    }
    $frontmatter = $Matches['fm']
    $body = $Matches['body'].Trim() + "`n"

    if ($frontmatter -notmatch 'name:\s*(?<name>\S+)') {
        throw "Missing 'name' in frontmatter of $srcPath"
    }
    $name = $Matches['name']
    if ($name -ne $agentName) {
        throw "Frontmatter name '$name' does not match file name '$agentName.md'"
    }

    if ($frontmatter -notmatch 'description:\s*(?<desc>.+)') {
        throw "Missing 'description' in frontmatter of $srcPath"
    }
    $description = $Matches['desc'].Trim()

    # Optional: only present when this agent must pin a specific reasoning
    # effort. Omitted by default, so Codex falls back to whatever is
    # selected in its own main screen for that session, matching how
    # Claude Code/Cursor subagents behave when no `model:` is set.
    $reasoningEffort = $null
    if ($frontmatter -match 'reasoning_effort:\s*(?<effort>\S+)') {
        $reasoningEffort = $Matches['effort'].Trim()
    }

    # 1) Claude Code and Cursor already share the same subagent format
    #    (Markdown + YAML frontmatter), so they get the exact same bytes.
    Write-TextFile (Join-Path $ClaudeDir "$agentName.md") $raw
    Write-TextFile (Join-Path $CursorDir "$agentName.md") $raw

    # 2) Codex needs TOML, so wrap the same body text instead of duplicating it by hand.
    $effortLine = ""
    if ($reasoningEffort) {
        $effortLine = "model_reasoning_effort = `"$reasoningEffort`"`n`n"
    }
    $toml = "$effortLine" + "developer_instructions = `"`"`"`n$body`"`"`"`n"
    Write-TextFile (Join-Path $CodexAgentsDir "$agentName.toml") $toml

    $configEntries += [PSCustomObject]@{ Name = $name; Description = $description }
}

# 3) Regenerate .codex/config.toml's agent table from the same source of truth.
$configLines = New-Object System.Collections.Generic.List[string]
$configLines.Add("[agents]")
$configLines.Add("enabled = true")
$configLines.Add("")
foreach ($entry in $configEntries) {
    $configLines.Add("[agents.$($entry.Name)]")
    $configLines.Add("description = `"$($entry.Description)`"")
    $configLines.Add("config_file = `"agents/$($entry.Name).toml`"")
    $configLines.Add("")
}
$configContent = ($configLines -join "`n").TrimEnd("`n") + "`n"
Write-TextFile $CodexConfigPath $configContent

Write-Host "Synced $($AgentOrder.Count) agents from docs/agents/ into .claude/agents, .cursor/agents, .codex/agents and .codex/config.toml"

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hookPath = Join-Path $projectRoot '.githooks\pre-push'
if (-not (Test-Path -LiteralPath $hookPath)) {
    throw "The versioned pre-push hook is missing: $hookPath"
}

$gitCommand = Get-Command git -ErrorAction SilentlyContinue
if ($null -eq $gitCommand) {
    throw 'Git was not found. Install Git or add it to PATH before installing hooks.'
}

& $gitCommand.Source -C $projectRoot config --local core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) {
    throw 'Git could not configure the project-local hooks path.'
}

Write-Host "Installed the TAD Game Basics pre-push gate for $projectRoot"

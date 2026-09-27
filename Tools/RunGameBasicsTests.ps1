[CmdletBinding()]
param(
    [ValidateRange(60, 3600)]
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$unityCommand = Get-Command unity -ErrorAction SilentlyContinue
if ($null -eq $unityCommand) {
    Write-Error 'Unity CLI was not found. Install it or add it to PATH before pushing.'
    exit 1
}

$unityExecutable = $unityCommand.Source

function Test-UnityProjectIsLocked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectRoot,

        [Parameter(Mandatory = $true)]
        [string]$UnityExecutable
    )

    $lockFile = Join-Path $ProjectRoot 'Temp\UnityLockfile'
    if (Test-Path -LiteralPath $lockFile) {
        return $true
    }

    try {
        $statusOutput = & $UnityExecutable status --project-path $ProjectRoot --format json --no-banner --non-interactive 2>$null
        if ($LASTEXITCODE -eq 0 -and $statusOutput) {
            $status = ($statusOutput -join [Environment]::NewLine) | ConvertFrom-Json -ErrorAction Stop
            if ($status.success -and $status.data.instances -and @($status.data.instances).Count -gt 0) {
                return $true
            }
        }
    }
    catch {
        Write-Verbose "Could not query the Unity editor state: $($_.Exception.Message)"
    }

    return $false
}

if (Test-UnityProjectIsLocked -ProjectRoot $projectRoot -UnityExecutable $unityExecutable) {
    Write-Error 'Game Basics tests were not started because the Unity Editor is using this project. Close Unity (and any other TAD Editor instance) and retry the push. The pre-push gate intentionally never bypasses tests while the project is locked.'
    exit 1
}

$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$resultsRoot = Join-Path $projectRoot (Join-Path 'Temp\GameBasicsTestResults' $runId)
New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null

function Assert-NUnitReportIsSuccessful {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReportPath,

        [Parameter(Mandatory = $true)]
        [string]$Mode
    )

    try {
        [xml]$document = Get-Content -LiteralPath $ReportPath -Raw
        $testRun = $document.SelectSingleNode('/test-run')
        if ($null -eq $testRun -or -not ($testRun -is [System.Xml.XmlElement])) {
            throw 'The NUnit report does not contain a test-run element.'
        }

        $counts = @{}
        foreach ($attributeName in @('total', 'failed', 'errors', 'inconclusive')) {
            $rawValue = $testRun.GetAttribute($attributeName)
            if ([string]::IsNullOrWhiteSpace($rawValue)) {
                if ($attributeName -eq 'total') {
                    throw 'The NUnit report does not state how many tests ran.'
                }

                $counts[$attributeName] = 0
                continue
            }

            $parsedValue = 0
            if (-not [int]::TryParse($rawValue, [ref]$parsedValue)) {
                throw "The NUnit report has an invalid $attributeName count: $rawValue"
            }

            $counts[$attributeName] = $parsedValue
        }

        if ($counts.total -le 0) {
            throw 'The NUnit report recorded zero tests. The Game Basics gate must not pass an empty suite.'
        }

        if ($counts.failed -gt 0 -or $counts.errors -gt 0 -or $counts.inconclusive -gt 0) {
            throw "The NUnit report is not successful (failed=$($counts.failed), errors=$($counts.errors), inconclusive=$($counts.inconclusive))."
        }
    }
    catch {
        throw "The $Mode NUnit report is invalid or unsuccessful: $($_.Exception.Message)"
    }
}

function Invoke-GameBasicsSuite {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('EditMode', 'PlayMode')]
        [string]$Mode
    )

    $nunitReport = Join-Path $resultsRoot "$Mode.nunit.xml"
    $junitReport = Join-Path $resultsRoot "$Mode.junit.xml"

    Write-Host "Running TAD $Mode Game Basics tests..."
    & $unityExecutable test $projectRoot --mode $Mode --filter 'TAD.Tests' --report-format nunit,junit --output $nunitReport --junit-output $junitReport --timeout $TimeoutSeconds --non-interactive --no-banner
    $testExitCode = $LASTEXITCODE

    if (-not (Test-Path -LiteralPath $nunitReport)) {
        Write-Error "Unity did not create the $Mode NUnit report: $nunitReport"
        exit 1
    }

    if (-not (Test-Path -LiteralPath $junitReport)) {
        Write-Error "Unity did not create the $Mode JUnit report: $junitReport"
        exit 1
    }

    Assert-NUnitReportIsSuccessful -ReportPath $nunitReport -Mode $Mode

    if ($testExitCode -ne 0) {
        Write-Error "$Mode Game Basics tests failed or could not complete. Reports: $resultsRoot"
        exit $testExitCode
    }

    Write-Host "$Mode Game Basics tests passed. Reports: $resultsRoot"
}

try {
    Invoke-GameBasicsSuite -Mode EditMode
    Invoke-GameBasicsSuite -Mode PlayMode
    Write-Host 'All TAD Game Basics tests passed.'
    exit 0
}
catch {
    Write-Error "The Game Basics test gate could not complete: $($_.Exception.Message)"
    exit 1
}

<#
.SYNOPSIS
Runs the checks that .github/workflows/ci.yml runs, locally.

.DESCRIPTION
Use it before pushing or merging a pull request while CI is not available. With -Puzzles it also runs the same checks in the sibling
E2e4.SlidingBlock.Puzzles repository, built against this checkout rather than the published package. Stops at the first failing step.
#>
[CmdletBinding()]
param(
    [switch] $Puzzles,
    [string] $PuzzlesPath
)

$ErrorActionPreference = 'Stop'

function Invoke-Step([string] $Name, [scriptblock] $Action) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "Step failed: $Name (exit code $LASTEXITCODE)" }
}

function Invoke-RepoChecks([string] $Root, [string] $Solution) {
    Push-Location $Root
    try {
        Write-Host "### $Solution" -ForegroundColor Green
        Invoke-Step 'Check formatting' { dotnet format whitespace $Solution --verify-no-changes }
        Invoke-Step 'Check type keywords' { dotnet format style $Solution --verify-no-changes --diagnostics IDE0049 --severity warn }
        Invoke-Step 'Test' { dotnet test $Solution --configuration Release }
    }
    finally { Pop-Location }
}

$logicRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
if (-not $PuzzlesPath) { $PuzzlesPath = Join-Path (Split-Path $logicRoot -Parent) 'E2e4.SlidingBlock.Puzzles' }
Invoke-RepoChecks $logicRoot 'E2e4.SlidingBlock.Logic.slnx'

Push-Location $logicRoot
try {
    Invoke-Step 'Check for vulnerable packages' {
        $report = dotnet list E2e4.SlidingBlock.Logic.slnx package --vulnerable --include-transitive 2>&1 | Out-String
        Write-Host $report
        if ($report -match 'has the following vulnerable packages') {
            Write-Host 'A dependency has a known vulnerability.' -ForegroundColor Red
            $global:LASTEXITCODE = 1
        }
        else { $global:LASTEXITCODE = 0 }
    }

    $packDir = Join-Path ([IO.Path]::GetTempPath()) "slidingblock-check-$([guid]::NewGuid().ToString('N'))"
    try {
        Invoke-Step 'Pack' {
            dotnet pack src/E2e4.SlidingBlock.Logic/E2e4.SlidingBlock.Logic.csproj --configuration Release --no-build --output $packDir
        }
        Invoke-Step 'Only System.Memory (netstandard2.0) as a dependency' {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $nupkg = Get-ChildItem $packDir -Filter 'E2e4.SlidingBlock.Logic.*.nupkg' | Where-Object Name -notlike '*.symbols.*' | Select-Object -First 1
            $zip = [IO.Compression.ZipFile]::OpenRead($nupkg.FullName)
            try {
                $entry = $zip.Entries | Where-Object FullName -eq 'E2e4.SlidingBlock.Logic.nuspec'
                $reader = [IO.StreamReader]::new($entry.Open())
                $nuspec = $reader.ReadToEnd()
                $reader.Dispose()
            }
            finally { $zip.Dispose() }
            # netstandard2.0 takes the one System package that gives it Span<T>; net8.0 and net10.0 take nothing.
            $allowed = 'System.Memory'
            $unexpected = ([xml] $nuspec).package.metadata.dependencies.group | ForEach-Object {
                $group = $_
                @($group.dependency) | Where-Object { $_ -and ($group.targetFramework -notlike '.NETStandard*' -or $_.id -notin $allowed) } |
                    ForEach-Object { "$($group.targetFramework): $($_.id)" }
            }
            if ($unexpected) {
                Write-Host "Unexpected package dependencies: $($unexpected -join ', ')" -ForegroundColor Red
                $global:LASTEXITCODE = 1
            }
            else { $global:LASTEXITCODE = 0 }
        }
    }
    finally { Remove-Item $packDir -Recurse -Force -ErrorAction SilentlyContinue }
}
finally { Pop-Location }

if ($Puzzles) {
    $env:UseLocalLogic = 'true'  # Puzzles uses the Logic package unless told to build the sibling checkout
    Invoke-RepoChecks (Resolve-Path $PuzzlesPath) 'E2e4.SlidingBlock.Puzzles.slnx'
}

Write-Host 'All checks passed.' -ForegroundColor Green

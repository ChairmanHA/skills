[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryPath = [IO.Path]::GetFullPath($RepositoryRoot)
$buildAllPath = Join-Path $repositoryPath 'scripts\build_all.bat'
$buildSucceeded = $false
$fatalError = $null
$smokeResults = @()
$temporaryRoot = $null

$russianProductName = -join (@(
    0x0421, 0x041F, 0x041E, 0x0020, 0x0413, 0x0421, 0x0420, 0x0412
) | ForEach-Object { [char]$_ })

$matrix = @(
    [pscustomobject]@{ Label = 'standard cn'; Packet = 'standard'; Language = 'cn'; Archive = 'SGStudio'; Executable = 'SGStudio.exe' },
    [pscustomobject]@{ Label = 'standard en'; Packet = 'standard'; Language = 'en'; Archive = 'SGStudio'; Executable = 'SGStudio.exe' },
    [pscustomobject]@{ Label = 'standard ru'; Packet = 'standard'; Language = 'ru'; Archive = 'SGStudio_russia'; Executable = "$russianProductName.exe" },
    [pscustomobject]@{ Label = 'neutral cn'; Packet = 'neutral'; Language = 'cn'; Archive = 'VSG'; Executable = 'VSG.exe' },
    [pscustomobject]@{ Label = 'neutral en'; Packet = 'neutral'; Language = 'en'; Archive = 'VSG'; Executable = 'VSG.exe' }
)

function Stop-TestProcessTree {
    param([Diagnostics.Process]$Process)

    if ($null -eq $Process) {
        return
    }

    try {
        $Process.Refresh()
        if ($Process.HasExited) {
            return
        }

        $taskkillPath = Join-Path $env:SystemRoot 'System32\taskkill.exe'
        & $taskkillPath /PID $Process.Id /T /F *> $null
        if ($LASTEXITCODE -ne 0) {
            Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        }
    }
    catch {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
    }
}

try {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw 'This final Release workflow can run only on Windows.'
    }

    if (-not (Test-Path -LiteralPath $buildAllPath -PathType Leaf)) {
        throw "build_all.bat was not found: $buildAllPath"
    }

    $productProcessNames = @('SGStudio', 'VSG', $russianProductName)
    $runningProducts = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $productProcessNames -contains $_.ProcessName
    })
    if ($runningProducts.Count -gt 0) {
        $runningSummary = ($runningProducts | ForEach-Object {
            '{0} (PID {1})' -f $_.ProcessName, $_.Id
        }) -join ', '
        throw "Close running product processes before packaging: $runningSummary"
    }

    Write-Host '== Final Win32 Release packaging =='
    Write-Host '  matrix    : standard cn/en/ru, neutral cn/en'
    Write-Host '  watermark : OFF'
    Write-Host '  BNC       : excluded'

    & $buildAllPath '--watermark' 'off'
    if ($LASTEXITCODE -ne 0) {
        throw "build_all.bat failed with exit code $LASTEXITCODE."
    }
    $buildSucceeded = $true

    $temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $temporaryRoot = Join-Path $temporaryBase ('sgstudio-release-smoke-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

    foreach ($target in $matrix) {
        $testProcess = $null
        $passed = $false
        $detail = $null

        try {
            $zipPath = Join-Path $repositoryPath (Join-Path 'build\windows_x86_64' (Join-Path $target.Packet (Join-Path $target.Language ($target.Archive + '.zip'))))
            if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
                throw "Archive not found: $zipPath"
            }

            $extractPath = Join-Path $temporaryRoot ($target.Packet + '-' + $target.Language)
            Expand-Archive -LiteralPath $zipPath -DestinationPath $extractPath -Force

            $archiveRoot = Join-Path $extractPath $target.Archive
            $binPath = Join-Path $archiveRoot 'bin'
            $executablePath = Join-Path $binPath $target.Executable
            if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
                throw "Executable not found after extraction: $executablePath"
            }

            Write-Host ("== Smoke test: {0} ==" -f $target.Label)
            $testProcess = Start-Process -FilePath $executablePath -WorkingDirectory $binPath -PassThru
            Start-Sleep -Seconds 6
            $testProcess.Refresh()

            if ($testProcess.HasExited) {
                throw "Process exited within six seconds (exit code $($testProcess.ExitCode))."
            }

            $passed = $true
            $detail = 'Process remained alive for six seconds.'
        }
        catch {
            $detail = $_.Exception.Message
        }
        finally {
            Stop-TestProcessTree -Process $testProcess
        }

        $smokeResults += [pscustomobject]@{
            Label = $target.Label
            Passed = $passed
            Detail = $detail
        }
    }
}
catch {
    $fatalError = $_.Exception.Message
}
finally {
    if ($null -ne $temporaryRoot -and (Test-Path -LiteralPath $temporaryRoot)) {
        $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
        $resolvedTemporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if ($resolvedTemporaryRoot.StartsWith($resolvedTemporaryBase, [StringComparison]::OrdinalIgnoreCase) -and
            -not $resolvedTemporaryRoot.Equals($resolvedTemporaryBase, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

$passedCount = @($smokeResults | Where-Object { $_.Passed }).Count
$allLaunchesSucceeded = $buildSucceeded -and $smokeResults.Count -eq $matrix.Count -and $passedCount -eq $matrix.Count

Write-Host ''
Write-Host '== FINAL RELEASE SUMMARY =='
Write-Host ('Compile/package : {0}' -f $(if ($buildSucceeded) { 'SUCCESS' } else { 'FAILED OR NOT RUN' }))
foreach ($target in $matrix) {
    $result = @($smokeResults | Where-Object { $_.Label -eq $target.Label })
    if ($result.Count -eq 0) {
        Write-Host ('Launch {0,-11}: NOT RUN' -f $target.Label)
    }
    elseif ($result[0].Passed) {
        Write-Host ('Launch {0,-11}: SUCCESS' -f $target.Label)
    }
    else {
        Write-Host ('Launch {0,-11}: FAILED - {1}' -f $target.Label, $result[0].Detail)
    }
}
Write-Host ('Launch check    : {0} ({1}/{2})' -f $(if ($allLaunchesSucceeded) { 'SUCCESS' } else { 'FAILED OR NOT RUN' }), $passedCount, $matrix.Count)

if ($null -ne $fatalError) {
    Write-Host ("Error           : $fatalError")
}

if ($buildSucceeded -and $allLaunchesSucceeded) {
    exit 0
}

exit 1

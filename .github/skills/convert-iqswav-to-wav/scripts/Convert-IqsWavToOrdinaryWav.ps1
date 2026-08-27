[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string[]]$InputPath,

    [string]$OutputDirectory,

    [switch]$InPlace,

    [switch]$Force,

    [switch]$AnalyzeOnly,

    [switch]$Recurse,

    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($AnalyzeOnly -and ($InPlace -or $Force -or $OutputDirectory)) {
    throw "-AnalyzeOnly cannot be combined with output or overwrite options."
}
if ($InPlace -and $OutputDirectory) {
    throw "-InPlace and -OutputDirectory are mutually exclusive."
}
if ($InPlace -and -not $Force) {
    throw "In-place conversion requires both -InPlace and -Force."
}

$sourcePath = Join-Path $PSScriptRoot "IqsWavConversion.cs"
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "Converter implementation is missing: $sourcePath"
}
if (-not ("IqsWavConversion" -as [type])) {
    Add-Type -Path $sourcePath
}

$pathComparer = if ([IO.Path]::DirectorySeparatorChar -eq "\") {
    [StringComparer]::OrdinalIgnoreCase
} else {
    [StringComparer]::Ordinal
}
$uniqueInputs = [System.Collections.Generic.HashSet[string]]::new($pathComparer)

foreach ($pendingPath in $InputPath) {
    $item = Get-Item -LiteralPath $pendingPath
    if ($item.PSIsContainer) {
        $children = Get-ChildItem -LiteralPath $item.FullName -File -Filter "*.wav" -Recurse:$Recurse
        foreach ($child in $children) {
            [void]$uniqueInputs.Add($child.FullName)
        }
    } else {
        [void]$uniqueInputs.Add($item.FullName)
    }
}

$resolvedInputs = @($uniqueInputs | Sort-Object)
if ($resolvedInputs.Count -eq 0) {
    throw "No WAV input files were found."
}

if ($AnalyzeOnly) {
    $reports = @(
        foreach ($inputFile in $resolvedInputs) {
            [IqsWavConversion]::Analyze($inputFile)
        }
    )
    if ($Json) {
        $reports | ConvertTo-Json -Depth 4
    } else {
        $reports
    }
    return
}

$outputRoot = $null
if ($OutputDirectory) {
    $outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
}

$jobs = @(
    foreach ($inputFile in $resolvedInputs) {
        $inputFullPath = [IO.Path]::GetFullPath($inputFile)
        if ($InPlace) {
            $outputFullPath = $inputFullPath
        } elseif ($outputRoot) {
            $outputFullPath = Join-Path $outputRoot ([IO.Path]::GetFileName($inputFullPath))
        } else {
            $directory = [IO.Path]::GetDirectoryName($inputFullPath)
            $stem = [IO.Path]::GetFileNameWithoutExtension($inputFullPath)
            $outputFullPath = Join-Path $directory ($stem + ".ordinary.wav")
        }

        [pscustomobject]@{
            Input = $inputFullPath
            Output = [IO.Path]::GetFullPath($outputFullPath)
        }
    }
)

$uniqueOutputs = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
foreach ($job in $jobs) {
    if (-not $uniqueOutputs.Add($job.Output)) {
        throw "Multiple inputs resolve to the same output: $($job.Output)"
    }
    if ($pathComparer.Equals($job.Input, $job.Output) -and -not $InPlace) {
        throw "An output path resolves to its input; use explicit -InPlace -Force instead: $($job.Input)"
    }
    if ((Test-Path -LiteralPath $job.Output) -and -not $Force) {
        throw "Output already exists; use -Force only after verifying overwrite intent: $($job.Output)"
    }
}

if ($outputRoot -and -not (Test-Path -LiteralPath $outputRoot)) {
    if ($PSCmdlet.ShouldProcess($outputRoot, "Create output directory")) {
        [void](New-Item -ItemType Directory -Path $outputRoot)
    }
}

$reports = @(
    foreach ($job in $jobs) {
        $action = if ($pathComparer.Equals($job.Input, $job.Output)) {
            "Replace IQS-WAV in place with validated ordinary WAV"
        } else {
            "Convert IQS-WAV to $($job.Output)"
        }
        if ($PSCmdlet.ShouldProcess($job.Input, $action)) {
            [IqsWavConversion]::Convert($job.Input, $job.Output, [bool]$Force)
        }
    }
)

if ($Json) {
    $reports | ConvertTo-Json -Depth 4
} else {
    $reports
}

param(
    [switch]$Test,
    [switch]$BuildOnly
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Join-Path $projectRoot "src"
$testSource = Join-Path $projectRoot "tests\CoreTests.cs"
$testProgram = Join-Path $projectRoot "tests\TestProgram.cs"
$manifest = Join-Path $sourceRoot "app.manifest"
$outputRoot = Join-Path $projectRoot "bin"
$appOutput = Join-Path $outputRoot "HkuLibrarySeatBooking.exe"
$testOutput = Join-Path $outputRoot "HkuLibrarySeatBooking.Tests.exe"
$testReport = Join-Path $outputRoot "self-test.txt"

$frameworkRoot = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path (Join-Path $frameworkRoot "csc.exe"))) {
    $frameworkRoot = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319"
}

$compiler = Join-Path $frameworkRoot "csc.exe"
if (-not (Test-Path $compiler)) {
    throw "Could not find the built-in .NET Framework compiler (csc.exe)."
}

$appSources = @(Get-ChildItem -Path (Join-Path $sourceRoot "*.cs") | Select-Object -ExpandProperty FullName)
$coreSourceNames = @(
    "BookingModels.cs",
    "BookingScheduler.cs",
    "ConfigStore.cs",
    "HkuBookingClient.cs",
    "ReleaseSchedule.cs",
    "WebFormParser.cs"
)
$testSources = @()
foreach ($sourceName in $coreSourceNames) {
    $testSources += Join-Path $sourceRoot $sourceName
}
$testSources += $testSource
$testSources += $testProgram
$buildInputs = @($appSources) + @($manifest)
$needsBuild = -not (Test-Path $appOutput)
if (-not $needsBuild) {
    $outputTime = (Get-Item $appOutput).LastWriteTimeUtc
    foreach ($inputFile in $buildInputs) {
        if ((Get-Item $inputFile).LastWriteTimeUtc -gt $outputTime) {
            $needsBuild = $true
            break
        }
    }
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$references = @(
    "/reference:System.dll",
    "/reference:System.Core.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Windows.Forms.dll",
    "/reference:System.Runtime.Serialization.dll",
    "/reference:System.Security.dll"
)

if ($needsBuild -or $Test) {
    $compileArguments = @(
        "/nologo",
        "/target:winexe",
        "/optimize+",
        "/codepage:65001",
        "/win32manifest:$manifest",
        "/out:$appOutput"
    ) + $references + $appSources
    & $compiler $compileArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Application build failed with exit code $LASTEXITCODE."
    }
}

if ($Test) {
    $testArguments = @(
        "/nologo",
        "/target:exe",
        "/optimize+",
        "/codepage:65001",
        "/out:$testOutput"
    ) + $references + $testSources
    & $compiler $testArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Test build failed with exit code $LASTEXITCODE."
    }

    & $testOutput --self-test $testReport
    $testExitCode = $LASTEXITCODE
    if (Test-Path $testReport) {
        Get-Content $testReport
    }
    if ($testExitCode -ne 0) {
        throw "Self-test failed with exit code $testExitCode."
    }
    Remove-Item -LiteralPath $testOutput -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $testReport -Force -ErrorAction SilentlyContinue
    Write-Host "Self-test passed."
}

if (-not $BuildOnly -and -not $Test) {
    Start-Process -FilePath $appOutput -WorkingDirectory $outputRoot
}

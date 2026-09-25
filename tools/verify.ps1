# Every check in one command, the way CI runs them.
#
#   powershell -File tools/verify.ps1
#
# - the three Node suites run as they are
# - the .NET suite is published as a single-file exe first, because a machine with Smart App
#   Control switched on refuses to load freshly built DLLs
# - the build-fact check inspects the published browser, if dist/ has one
param(
    [switch]$SkipBuildFacts
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $root "scratch\tests"
New-Item -ItemType Directory -Force -Path $scratch | Out-Null

$failures = 0

function Run([string]$label, [scriptblock]$body) {
    "`n=== $label ==="
    try { & $body } catch { $script:failures++; "FAILED: $_" }
}

Run "command bar, calculator, converter, palette, motion helpers (27)" {
    $exe = Join-Path $scratch "CommandTests.exe"
    & dotnet publish (Join-Path $root "tools\CommandTests\CommandTests.csproj") -c Release -r win-x64 `
        --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $scratch -v q | Out-Null
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "CommandTests failed" }
}

Run "find bar logic (16)" {
    & node (Join-Path $root "tools\FindLogicTest.js")
    if ($LASTEXITCODE -ne 0) { throw "FindLogicTest failed" }
}

Run "private home page (13)" {
    & node (Join-Path $root "tools\PrivatePageTest.js")
    if ($LASTEXITCODE -ne 0) { throw "PrivatePageTest failed" }
}

Run "water physics (11)" {
    & node (Join-Path $root "tools\WaterPhysicsTest.js")
    if ($LASTEXITCODE -ne 0) { throw "WaterPhysicsTest failed" }
}

if (-not $SkipBuildFacts) {
    $dist = Join-Path $root "dist\Nibble.exe"
    if (Test-Path $dist) {
        Run "build facts" {
            # the object folder carries the assembly-level facts a single-file exe hides
            $objects = Join-Path $root "src\Nibble\obj\Release\net8.0-windows\win-x64"
            $arguments = @($dist)
            if (Test-Path $objects) { $arguments += @("--object-dir", $objects) }
            & dotnet run --project (Join-Path $root "tools\VerifyBuild") -c Release -- @arguments
            if ($LASTEXITCODE -ne 0) { throw "VerifyBuild failed" }
        }
    } else {
        "`n=== build facts ===`nno dist/Nibble.exe yet - run tools/package.ps1 first (skipped)"
    }
}

"`n" + $(if ($failures -eq 0) { "EVERYTHING PASSED" } else { "$failures SUITE(S) FAILED" })
exit $failures

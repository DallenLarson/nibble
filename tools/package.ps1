# Builds everything that goes out the door: the browser, the docs beside it, and (if Inno
# Setup 6 is installed) the installer.
#
#   powershell -File tools/package.ps1                 -> dist/
#   powershell -File tools/package.ps1 -SkipInstaller  -> dist/ without Setup.exe
param(
    [string]$Out = "dist",
    [switch]$SkipInstaller,
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "src\Nibble\Nibble.csproj"
$outDir = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $root $Out }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$selfContainedArg = "false"
if ($SelfContained) { $selfContainedArg = "true" }
$publishArgs = @(
    "publish", $project, "-c", "Release", "-r", "win-x64",
    "--self-contained", $selfContainedArg,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true"
)
if ($SelfContained) { $publishArgs += "-p:EnableCompressionInSingleFile=true" }
$publishArgs += @("-o", $outDir)

"building the browser..."
& dotnet @publishArgs | Select-String -Pattern "error|warning|Nibble ->" | ForEach-Object { $_.Line }
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

"copying the documentation and licences..."
foreach ($file in @("README.md", "CHANGELOG.md", "LICENSE", "Icons-LICENSE.txt",
                    "THIRD-PARTY-NOTICES.txt", "Silkscreen-OFL.txt", "Monocraft-OFL.txt")) {
    Copy-Item (Join-Path $root $file) (Join-Path $outDir $file) -Force
}
Copy-Item (Join-Path $root "assets\nibble.png") (Join-Path $outDir "nibble.png") -Force
Copy-Item (Join-Path $root "assets\nibble-logo.png") (Join-Path $outDir "nibble-logo.png") -Force
Copy-Item (Join-Path $root "assets\icon-set.png") (Join-Path $outDir "icon-set.png") -Force

# The README links its pictures by the paths they have in the repository, so the copies the
# two folders hold travel with it - otherwise the README next to the shipped exe is full of
# broken images.
foreach ($folder in @("assets", "screenshots")) {
    $from = Join-Path $root $folder
    if (-not (Test-Path $from)) { continue }
    $to = Join-Path $outDir $folder
    New-Item -ItemType Directory -Force -Path $to | Out-Null
    Get-ChildItem -LiteralPath $from -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($from.Length).TrimStart('\')
        $target = Join-Path $to $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    }
}

$exe = Join-Path $outDir "Nibble.exe"
"browser: $exe ({0:N0} bytes)" -f (Get-Item $exe).Length

if ($SkipInstaller) { "installer: skipped"; exit 0 }

# Inno Setup is not a build dependency of the browser, only of the installer.
$iscc = @(
    "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $iscc) {
    "installer: Inno Setup 6 not found - skipping Setup.exe"
    "           install it from https://jrsoftware.org/isdl.php and run this again"
    exit 0
}

"building the installer with $iscc ..."
& $iscc (Join-Path $root "installer\Nibble.iss") | Select-String -Pattern "Successful|Error|Warning" |
    ForEach-Object { $_.Line }
if ($LASTEXITCODE -ne 0) { throw "installer build failed" }

Get-ChildItem $outDir | Sort-Object Name | Select-Object Length, Name | Format-Table -AutoSize

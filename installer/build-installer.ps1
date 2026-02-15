param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$SkipInnoSetup
)

$ErrorActionPreference = "Stop"

$RootDir = Split-Path -Parent $PSScriptRoot
$StagingDir = Join-Path $PSScriptRoot "staging"
$DistDir = Join-Path $PSScriptRoot "dist"

function Remove-ItemSafe {
    param([string]$Path)
    if (Test-Path $Path) {
        Remove-Item -Path $Path -Recurse -Force
    }
}

Remove-ItemSafe $StagingDir
New-Item -ItemType Directory -Force -Path (Join-Path $StagingDir "bin") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $StagingDir "packages") | Out-Null

$ProjectPath = Join-Path $RootDir "native\yasn-native\yasn-native.csproj"
Write-Host "[yasn] Publishing $Runtime..."
dotnet publish $ProjectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:PublishTrimmed=false

$PublishDir = Join-Path $RootDir "native\yasn-native\bin\$Configuration\net10.0\$Runtime\publish"
$YasnExe = Join-Path $PublishDir "yasn.exe"
if (-not (Test-Path $YasnExe)) {
    throw "Published binary not found: $YasnExe"
}

Copy-Item -Path $YasnExe -Destination (Join-Path $StagingDir "bin\yasn.exe") -Force

$UiSdkSrc = Join-Path $RootDir "packages\ui-sdk"
$UiKitSrc = Join-Path $RootDir "packages\ui-kit"
$UiSdkDest = Join-Path $StagingDir "packages\ui-sdk"
$UiKitDest = Join-Path $StagingDir "packages\ui-kit"

if (Test-Path $UiSdkSrc) {
    $null = robocopy $UiSdkSrc $UiSdkDest /E /XD node_modules /NFL /NDL /NJH /NJS /nc /ns /np
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for ui-sdk" }
}
if (Test-Path $UiKitSrc) {
    $null = robocopy $UiKitSrc $UiKitDest /E /XD node_modules /NFL /NDL /NJH /NJS /nc /ns /np
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for ui-kit" }
}

function Find-Iscc {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "C:\InnoSetup6\ISCC.exe"
    )
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { $candidates = @($cmd.Source) + $candidates }
    $reg = Get-ItemProperty "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*" -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like "*Inno Setup*" } | Select-Object -First 1
    if ($reg -and $reg.InstallLocation) { $candidates += Join-Path $reg.InstallLocation "ISCC.exe" }
    $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

$IsccPath = Find-Iscc

if (-not $IsccPath -and -not $SkipInnoSetup) {
    Write-Host "[yasn] Inno Setup 6 not found. Installing via winget..."
    winget install JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements --silent
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install Inno Setup. Install manually from https://jrsoftware.org/isinfo.php"
    }
    $IsccPath = Find-Iscc
    if (-not $IsccPath) {
        throw "Inno Setup installed but ISCC.exe not found. Restart terminal and retry."
    }
}

if ($SkipInnoSetup) {
    Write-Host "[yasn] Staging ready: $StagingDir (Inno Setup skipped)"
    exit 0
}
if ($IsccPath) {
    $IssPath = Join-Path $PSScriptRoot "yasn.iss"
    Push-Location $PSScriptRoot
    try {
        & $IsccPath $IssPath
        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }

    $SetupExe = Join-Path $DistDir "Yasn-Setup-0.1.0.exe"
    if (Test-Path $SetupExe) {
        Write-Host "[yasn] Installer: $SetupExe"
    }
}

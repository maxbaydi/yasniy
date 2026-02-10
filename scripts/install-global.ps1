param(
    [string]$Runtime,
    [string]$Configuration = "Release",
    [switch]$SkipPathUpdate
)

$ErrorActionPreference = "Stop"

function Get-DefaultRuntime {
    $arch = $env:PROCESSOR_ARCHITECTURE
    if ($arch -eq "ARM64") {
        return "win-arm64"
    }

    return "win-x64"
}

if ([string]::IsNullOrWhiteSpace($Runtime)) {
    $Runtime = Get-DefaultRuntime
}

$project = Join-Path $PSScriptRoot "..\native\yasn-native\yasn-native.csproj"
$project = (Resolve-Path $project).Path

Write-Host "[yasn] Publishing native toolchain ($Runtime)..."
dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:PublishTrimmed=false

$publishDir = Join-Path (Split-Path $project -Parent) "bin\$Configuration\net10.0\$Runtime\publish"
$sourceExe = Join-Path $publishDir "yasn.exe"
if (-not (Test-Path $sourceExe)) {
    throw "Не найден опубликованный бинарник: $sourceExe"
}

$localAppData = $env:LOCALAPPDATA
if ([string]::IsNullOrWhiteSpace($localAppData)) {
    $localAppData = Join-Path $env:USERPROFILE "AppData\Local"
}

$installDir = Join-Path $localAppData "yasn\bin"
New-Item -ItemType Directory -Force -Path $installDir | Out-Null

$targetExe = Join-Path $installDir "yasn.exe"
Copy-Item -Path $sourceExe -Destination $targetExe -Force

if (-not $SkipPathUpdate) {
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $pathItems = @()
    if (-not [string]::IsNullOrWhiteSpace($userPath)) {
        $pathItems = $userPath.Split(';', [System.StringSplitOptions]::RemoveEmptyEntries)
    }

    $exists = $pathItems | Where-Object { $_.TrimEnd('\\') -ieq $installDir.TrimEnd('\\') }
    if (-not $exists) {
        $newUserPath = if ([string]::IsNullOrWhiteSpace($userPath)) {
            $installDir
        }
        else {
            "$userPath;$installDir"
        }
        [Environment]::SetEnvironmentVariable("Path", $newUserPath, "User")
    }

    if (-not (($env:Path -split ';') | Where-Object { $_.TrimEnd('\\') -ieq $installDir.TrimEnd('\\') })) {
        $env:Path = "$installDir;$env:Path"
    }
}

Write-Host "[yasn] Installed: $targetExe"
Write-Host "[yasn] Version check:"
& $targetExe version
Write-Host "[yasn] Ready. If 'yasn' is not visible in old terminals, reopen them."

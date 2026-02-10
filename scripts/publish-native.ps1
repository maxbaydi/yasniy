param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "..\native\yasn-native\yasn-native.csproj"
$project = (Resolve-Path $project).Path

Write-Host "[yasn-native] Publishing $project for runtime $Runtime ..."

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:PublishTrimmed=false

Write-Host "[yasn-native] Done."

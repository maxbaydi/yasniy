param(
    [switch]$Editable,
    [switch]$Pipx
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Args
    )
    & $Command @Args
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed (exit $LASTEXITCODE): $Command $($Args -join ' ')"
    }
}

function Normalize-PathToken([string]$PathValue) {
    if (-not $PathValue) {
        return ""
    }
    return $PathValue.Trim().TrimEnd("\").ToLowerInvariant()
}

function Test-PathInList([string]$PathValue, [string]$PathList) {
    $target = Normalize-PathToken $PathValue
    if (-not $target) {
        return $false
    }
    foreach ($item in ($PathList -split ";")) {
        if ((Normalize-PathToken $item) -eq $target) {
            return $true
        }
    }
    return $false
}

function Get-PythonUserScriptsDir {
    $scriptsDir = python -c "import sysconfig; print(sysconfig.get_path('scripts', scheme='nt_user') or '')"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to resolve Python scripts directory."
    }
    $scriptsDir = $scriptsDir.Trim()
    if (-not $scriptsDir) {
        throw "Cannot resolve Python user scripts directory."
    }
    return $scriptsDir
}

function Ensure-UserPathContains([string]$Dir) {
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if (-not $userPath) {
        $userPath = ""
    }

    if (-not (Test-PathInList $Dir $userPath)) {
        $newPath = if ($userPath) { "$userPath;$Dir" } else { $Dir }
        [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
        Write-Host "[yasn] Added to USER PATH: $Dir"
    } else {
        Write-Host "[yasn] USER PATH already contains: $Dir"
    }

    if (-not (Test-PathInList $Dir $env:Path)) {
        $env:Path = "$env:Path;$Dir"
        Write-Host "[yasn] Added to current session PATH: $Dir"
    } else {
        Write-Host "[yasn] Current session PATH already contains: $Dir"
    }
}

$pythonScriptsDir = Get-PythonUserScriptsDir
Ensure-UserPathContains $pythonScriptsDir

if ($Pipx) {
    Write-Host "[yasn] Installing with pipx..."
    Invoke-Checked python -m pip install --user --no-warn-script-location pipx
    Invoke-Checked python -m pipx ensurepath
    Invoke-Checked python -m pipx install --force --name yasn $root
    Write-Host "[yasn] Done. Open a new terminal and run: yasn --help"
    exit 0
}

if ($Editable) {
    Write-Host "[yasn] Installing editable package (--user -e)..."
    Invoke-Checked python -m pip install --user --no-warn-script-location -e $root
} else {
    Write-Host "[yasn] Installing package (--user)..."
    Invoke-Checked python -m pip install --user --no-warn-script-location $root
}

Write-Host "[yasn] Done. Check:"
Write-Host "  yasn --help"
Write-Host "[yasn] Legacy alias remains available: yasny --help"

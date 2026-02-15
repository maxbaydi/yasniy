param(
    [ValidateSet("major", "minor", "patch", "custom")]
    [string]$Level,
    [string]$Comment,
    [string]$NewVersion,
    [string]$Branch = "main",
    [string]$VersionFile = "installer/yasn.iss",
    [string]$TagPrefix = "",
    [switch]$SkipPull,
    [switch]$DryRun,
    [switch]$AllowUntracked
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Args
    )

    & git @Args
    if ($LASTEXITCODE -ne 0) {
        throw "git command failed: git $($Args -join ' ')"
    }
}

function Get-GitText {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Args
    )

    $output = & git @Args
    if ($LASTEXITCODE -ne 0) {
        throw "failed to run git $($Args -join ' ')"
    }

    return (($output | Out-String).Trim())
}

function Read-RequiredInput {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prompt
    )

    while ($true) {
        $value = (Read-Host $Prompt).Trim()
        if ($value) {
            return $value
        }

        Write-Host "Value cannot be empty." -ForegroundColor Yellow
    }
}

function Resolve-Level {
    param(
        [string]$Current
    )

    if ($Current) {
        return $Current
    }

    Write-Host "Select version bump type:"
    Write-Host "  1) major (X+1.0.0)"
    Write-Host "  2) minor (X.Y+1.0)"
    Write-Host "  3) patch (X.Y.Z+1)"
    Write-Host "  4) custom (manual version)"

    while ($true) {
        $choice = (Read-Host "Enter [1-4]").Trim()
        switch ($choice) {
            "1" { return "major" }
            "2" { return "minor" }
            "3" { return "patch" }
            "4" { return "custom" }
            default { Write-Host "Invalid choice. Use 1, 2, 3, or 4." -ForegroundColor Yellow }
        }
    }
}

function Parse-CoreVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    if ($Version -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?<suffix>[-+][0-9A-Za-z.-]+)?$') {
        throw "Version '$Version' is not semver-like. Expected 1.2.3 or 1.2.3-rc.1."
    }

    return @{
        Major = [int]$Matches["major"]
        Minor = [int]$Matches["minor"]
        Patch = [int]$Matches["patch"]
    }
}

function Get-BumpedVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CurrentVersion,
        [Parameter(Mandatory = $true)]
        [ValidateSet("major", "minor", "patch", "custom")]
        [string]$SelectedLevel,
        [string]$ManualVersion
    )

    if ($SelectedLevel -eq "custom") {
        $candidate = if ($ManualVersion) { $ManualVersion.Trim() } else { Read-RequiredInput "Enter new version (e.g. 2.1.2)" }
        Parse-CoreVersion -Version $candidate | Out-Null
        return $candidate
    }

    $parts = Parse-CoreVersion -Version $CurrentVersion
    switch ($SelectedLevel) {
        "major" { return "$($parts.Major + 1).0.0" }
        "minor" { return "$($parts.Major).$($parts.Minor + 1).0" }
        "patch" { return "$($parts.Major).$($parts.Minor).$($parts.Patch + 1)" }
    }

    throw "Unknown version bump type: $SelectedLevel"
}

function Test-TagExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tag
    )

    & git rev-parse -q --verify "refs/tags/$Tag" *> $null
    if ($LASTEXITCODE -eq 0) {
        return $true
    }

    $remoteTag = & git ls-remote --tags origin "refs/tags/$Tag"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to check tags on origin."
    }

    return [bool]($remoteTag -join "")
}

$repoRoot = Get-GitText -Args @("rev-parse", "--show-toplevel")
if (-not $repoRoot) {
    throw "Failed to resolve repository root."
}

$porcelain = Get-GitText -Args @("status", "--porcelain")
$blockingChanges = if ($AllowUntracked) {
    ($porcelain -split "`n" | Where-Object { $_ -and $_ -notmatch '^\?\?' }) -join "`n"
} else {
    $porcelain
}
if ($blockingChanges) {
    throw "Working tree is not clean. Commit or stash current changes and rerun."
}

$currentBranch = Get-GitText -Args @("rev-parse", "--abbrev-ref", "HEAD")
if ($currentBranch -ne $Branch) {
    Write-Host "Switching to '$Branch'..."

    & git show-ref --verify --quiet "refs/heads/$Branch"
    if ($LASTEXITCODE -eq 0) {
        Invoke-Git -Args @("switch", $Branch)
    } else {
        Invoke-Git -Args @("switch", "-c", $Branch, "--track", "origin/$Branch")
    }
}

if (-not $SkipPull) {
    Write-Host "Pulling latest '$Branch' from origin..."
    Invoke-Git -Args @("pull", "--ff-only", "origin", $Branch)
}

$resolvedVersionFile = Join-Path $repoRoot $VersionFile
if (-not (Test-Path $resolvedVersionFile)) {
    throw "Version file not found: $resolvedVersionFile"
}

$versionText = [System.IO.File]::ReadAllText($resolvedVersionFile)
$versionRegex = [regex]'(?m)^(#define\s+MyAppVersion\s+")([^"]+)(")'
$currentVersionMatch = $versionRegex.Match($versionText)
if (-not $currentVersionMatch.Success) {
    throw "MyAppVersion line was not found in $VersionFile"
}

$currentVersion = $currentVersionMatch.Groups[2].Value
$resolvedLevel = Resolve-Level -Current $Level
$targetVersion = Get-BumpedVersion -CurrentVersion $currentVersion -SelectedLevel $resolvedLevel -ManualVersion $NewVersion
$releaseComment = if ($Comment) { $Comment.Trim() } else { Read-RequiredInput "Enter release comment" }
$commitMessage = "$targetVersion - $releaseComment"
$tagName = "$TagPrefix$targetVersion"

& git check-ref-format --allow-onelevel "refs/tags/$tagName" *> $null
if ($LASTEXITCODE -ne 0) {
    throw "Tag '$tagName' is not a valid git tag name."
}

if (Test-TagExists -Tag $tagName) {
    throw "Tag '$tagName' already exists (local or origin). Choose another version."
}

Write-Host ""
Write-Host "Current version: $currentVersion"
Write-Host "Next version:    $targetVersion"
Write-Host "Commit message:  $commitMessage"
Write-Host "Release tag:     $tagName"
Write-Host "Push branch:     $Branch"
Write-Host ""

if (-not $DryRun) {
    $confirm = (Read-Host "Continue? [y/N]").Trim().ToLowerInvariant()
    if ($confirm -notin @("y", "yes")) {
        Write-Host "Cancelled."
        exit 0
    }
}

$updatedText = $versionRegex.Replace(
    $versionText,
    {
        param($match)
        "$($match.Groups[1].Value)$targetVersion$($match.Groups[3].Value)"
    },
    1
)

if ($DryRun) {
    Write-Host "Dry-run enabled: no file changes, no commit, no tag, no push."
    exit 0
}

[System.IO.File]::WriteAllText($resolvedVersionFile, $updatedText, [System.Text.UTF8Encoding]::new($false))

Invoke-Git -Args @("add", "--", $VersionFile)
$stagedFiles = Get-GitText -Args @("diff", "--cached", "--name-only")
if (-not $stagedFiles) {
    throw "No staged changes after updating version."
}

Invoke-Git -Args @("commit", "-m", $commitMessage)
Invoke-Git -Args @("tag", "-a", $tagName, "-m", "Release $targetVersion")
Invoke-Git -Args @("push", "origin", $Branch)
Invoke-Git -Args @("push", "origin", $tagName)

Write-Host ""
Write-Host "Done:"
Write-Host "  - Commit: $commitMessage"
Write-Host "  - Tag:    $tagName"
Write-Host "  - Pushed: origin/$Branch and origin/$tagName"

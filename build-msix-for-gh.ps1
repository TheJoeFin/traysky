[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    # Root of the Traysky repository to build. Defaults to the folder this script lives in.
    [string]$RepoRoot = $PSScriptRoot,

    # Release notes for the GitHub release. When omitted, GitHub generates notes
    # from the commits/PRs since the previous release.
    [string]$Notes,

    # Mark the GitHub release as a pre-release.
    [switch]$Prerelease,

    # Build and sign only; skip tagging and the GitHub release.
    [switch]$SkipRelease,

    # Allow releasing with uncommitted changes (they would be in the build but not in the tag).
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $RepoRoot)) {
    throw "Repository root not found: $RepoRoot"
}

$repoRoot = (Resolve-Path -LiteralPath $RepoRoot).ProviderPath
$projectPath = Join-Path $repoRoot 'Traysky\Traysky.csproj'
$manifestPath = Join-Path $repoRoot 'Traysky\Package.appxmanifest'
$outputRoot = Join-Path $repoRoot 'Traysky\AppPackages\GitHub'
$publishProfilesRoot = Join-Path $repoRoot 'Traysky\Properties\PublishProfiles'

# Azure Trusted Signing (Artifact Signing) configuration.
$signtoolPath = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe'
$signingDlibPath = 'C:\Users\josep\AppData\Local\Microsoft\MicrosoftArtifactSigningClientTools\Azure.CodeSigning.Dlib.dll'
$signingMetadataPath = 'C:\Users\josep\OneDrive\Projects\Development\Apps\metadata.json'
$timestampUrl = 'http://timestamp.acs.microsoft.com'

$builds = @(
    @{
        Name = 'x64'
        Platform = 'x64'
        RuntimeIdentifier = 'win-x64'
        PublishProfile = 'win-x64.pubxml'
        OutputDir = (Join-Path $outputRoot 'x64')
    },
    @{
        Name = 'arm64'
        Platform = 'ARM64'
        RuntimeIdentifier = 'win-arm64'
        PublishProfile = 'win-arm64.pubxml'
        OutputDir = (Join-Path $outputRoot 'arm64')
    }
)

function Get-ReleaseTag {
    param(
        [Parameter(Mandatory)]
        [string]$Version
    )

    # 0.1.0.0 -> v0.1.0 (matches existing tags); keep a non-zero fourth part.
    $parts = $Version.Split('.')
    if ($parts.Count -eq 4 -and $parts[3] -eq '0') {
        $parts = $parts[0..2]
    }
    return 'v' + ($parts -join '.')
}

function Assert-ReleaseReady {
    param(
        [Parameter(Mandatory)]
        [string]$Tag
    )

    foreach ($tool in 'git', 'gh') {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
            throw "$tool is required but was not found on PATH."
        }
    }

    & gh auth status *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'gh is not authenticated. Run "gh auth login" first.'
    }

    $branch = (& git -C $repoRoot rev-parse --abbrev-ref HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $branch -eq 'HEAD') {
        throw 'Could not determine the current branch (detached HEAD?).'
    }

    if (-not $AllowDirty) {
        $status = & git -C $repoRoot status --porcelain
        if ($status) {
            throw "Working tree has uncommitted changes; commit/stash them or pass -AllowDirty.`n$($status -join "`n")"
        }
    }

    & git -C $repoRoot fetch origin --quiet
    if ($LASTEXITCODE -ne 0) {
        throw 'git fetch origin failed.'
    }

    $upstream = & git -C $repoRoot rev-parse --abbrev-ref '@{u}' 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Branch '$branch' has no upstream. Push it first (git push -u origin $branch)."
    }
    $ahead = [int](& git -C $repoRoot rev-list --count '@{u}..HEAD')
    if ($ahead -gt 0) {
        throw "Branch '$branch' is $ahead commit(s) ahead of $upstream. Push before releasing."
    }

    & git -C $repoRoot rev-parse -q --verify "refs/tags/$Tag" *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "Tag $Tag already exists locally. Bump the version in Package.appxmanifest."
    }
    $remoteTag = & git -C $repoRoot ls-remote --tags origin "refs/tags/$Tag"
    if ($remoteTag) {
        throw "Tag $Tag already exists on origin. Bump the version in Package.appxmanifest."
    }

    # Drafts have no tag on origin yet, so also check for an existing (draft) release.
    Push-Location -LiteralPath $repoRoot
    try {
        # Filter in PowerShell rather than --jq: Windows PowerShell 5.1 strips embedded quotes from native args.
        $releasesJson = & gh release list --limit 100 --json tagName
        if ($LASTEXITCODE -ne 0) {
            throw 'gh release list failed.'
        }
    }
    finally {
        Pop-Location
    }
    $existingRelease = ($releasesJson | Out-String | ConvertFrom-Json) | Where-Object { $_.tagName -eq $Tag }
    if ($existingRelease) {
        throw "A release for $Tag already exists on GitHub (possibly a draft). Delete it or bump the version."
    }

    return $branch
}

function Invoke-GitHubMsixBuild {
    param(
        [Parameter(Mandatory)]
        [hashtable]$Build
    )

    $appxPackageDir = (Join-Path $Build.OutputDir '')

    if (Test-Path -LiteralPath $Build.OutputDir) {
        Remove-Item -LiteralPath $Build.OutputDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Build.OutputDir -Force | Out-Null

    $arguments = @(
        'publish'
        $projectPath
        '-c'
        $Configuration
        "-p:Platform=$($Build.Platform)"
        "-p:RuntimeIdentifier=$($Build.RuntimeIdentifier)"
        "-p:PublishProfile=$(Join-Path $publishProfilesRoot $Build.PublishProfile)"
        '-p:GenerateAppxPackageOnBuild=true'
        '-p:UapAppxPackageBuildMode=SideloadOnly'
        '-p:AppxBundle=Never'
        '-p:AppxPackageSigningEnabled=false'
        '-p:PackageCertificateThumbprint='
        "-p:AppxPackageDir=$appxPackageDir"
    )

    Write-Host "Building GitHub MSIX for $($Build.Name)..."
    & dotnet @arguments

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $($Build.Name)."
    }
}

function Invoke-MsixSigning {
    param(
        [Parameter(Mandatory)]
        [System.IO.FileInfo[]]$Files
    )

    foreach ($file in $Files) {
        Write-Host "Signing $($file.Name)..."
        & $signtoolPath sign `
            /v /fd SHA256 `
            /tr $timestampUrl `
            /td SHA256 `
            /dlib $signingDlibPath `
            /dmdf $signingMetadataPath `
            $file.FullName

        if ($LASTEXITCODE -ne 0) {
            throw "signtool failed for $($file.Name) (exit code $LASTEXITCODE)."
        }
    }
}

$scriptFailed = $false
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'The dotnet SDK is required but was not found on PATH.'
    }

    foreach ($requiredFile in $projectPath, $manifestPath) {
        if (-not (Test-Path -LiteralPath $requiredFile)) {
            throw "Required file not found: $requiredFile"
        }
    }

    # The publish profiles are git-ignored (*.pubxml), so a fresh clone won't have them.
    foreach ($build in $builds) {
        $profilePath = Join-Path $publishProfilesRoot $build.PublishProfile
        if (-not (Test-Path -LiteralPath $profilePath)) {
            throw "Publish profile not found: $profilePath"
        }
    }

    foreach ($signingFile in $signtoolPath, $signingDlibPath, $signingMetadataPath) {
        if (-not (Test-Path -LiteralPath $signingFile)) {
            throw "Signing prerequisite not found: $signingFile"
        }
    }

    $manifestContent = [System.IO.File]::ReadAllText($manifestPath)
    $versionMatch = [regex]::Match($manifestContent, '(?s)<Identity\b[^>]*?\bVersion="([^"]+)"')
    if (-not $versionMatch.Success) {
        throw "Could not find Version attribute on <Identity> in $manifestPath."
    }
    $version = $versionMatch.Groups[1].Value
    $tag = Get-ReleaseTag -Version $version
    $versionedOutputDir = Join-Path $outputRoot "$version-gh"

    # Check git/gh state before the (slow) build so problems surface immediately.
    if (-not $SkipRelease) {
        $branch = Assert-ReleaseReady -Tag $tag
        Write-Host "Releasing $tag from branch '$branch'."
    }

    foreach ($build in $builds) {
        Invoke-GitHubMsixBuild -Build $build
    }

    Write-Host 'Collecting MSIX packages...'
    if (Test-Path -LiteralPath $versionedOutputDir) {
        Get-ChildItem -LiteralPath $versionedOutputDir -Filter '*.msix' -File -ErrorAction SilentlyContinue |
            Remove-Item -Force
    }
    else {
        New-Item -ItemType Directory -Path $versionedOutputDir -Force | Out-Null
    }
    foreach ($build in $builds) {
        # Exclude Dependencies\* (e.g. Microsoft.WindowsAppRuntime.2.msix) - only collect Traysky's own package.
        $msixFiles = Get-ChildItem -LiteralPath $build.OutputDir -Recurse -Filter 'Traysky_*.msix' -File
        if (-not $msixFiles) {
            throw "No .msix file was found under $($build.OutputDir) for $($build.Name)."
        }
        foreach ($msix in $msixFiles) {
            $destination = Join-Path $versionedOutputDir $msix.Name
            Copy-Item -LiteralPath $msix.FullName -Destination $destination -Force
            Write-Host "  Copied $($msix.Name) to $versionedOutputDir"
        }
    }

    $packages = @(Get-ChildItem -LiteralPath $versionedOutputDir -Filter '*.msix' -File)
    Invoke-MsixSigning -Files $packages
    Write-Host "Signed MSIX packages are under $versionedOutputDir"

    if (-not $SkipRelease) {
        $commit = (& git -C $repoRoot rev-parse HEAD).Trim()

        # Local-only tag; GitHub creates the remote tag at $commit when the draft is published.
        Write-Host "Creating local tag $tag at HEAD of '$branch' ($commit)..."
        & git -C $repoRoot tag $tag $commit
        if ($LASTEXITCODE -ne 0) {
            throw "git tag $tag failed."
        }

        # Always a draft: review and publish it manually on GitHub.
        $ghArguments = @('release', 'create', $tag) + $packages.FullName + @('--title', $tag, '--target', $commit, '--draft')
        $notesFile = $null
        if ($Notes) {
            # Pass notes via a file so quotes/newlines survive Windows PowerShell 5.1 native arg passing.
            $notesFile = [System.IO.Path]::GetTempFileName()
            [System.IO.File]::WriteAllText($notesFile, $Notes, [System.Text.UTF8Encoding]::new($false))
            $ghArguments += @('--notes-file', $notesFile)
        }
        else {
            $ghArguments += '--generate-notes'
        }
        if ($Prerelease) {
            $ghArguments += '--prerelease'
        }
        Write-Host "Creating draft GitHub release $tag..."
        Push-Location -LiteralPath $repoRoot
        try {
            & gh @ghArguments
            if ($LASTEXITCODE -ne 0) {
                throw "gh release create failed. Remove the local tag before retrying: git tag -d $tag"
            }
        }
        finally {
            Pop-Location
            if ($notesFile) {
                Remove-Item -LiteralPath $notesFile -Force -ErrorAction SilentlyContinue
            }
        }

        Write-Host "Done. Draft release $tag is saved; publishing it on GitHub will create the tag at $commit."
    }
    else {
        Write-Host 'Done.'
    }
}
catch {
    $scriptFailed = $true
    Write-Host ''
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
}

if ($scriptFailed -and $Host.Name -eq 'ConsoleHost') {
    Write-Host ''
    Read-Host 'Build failed. Press Enter to close this window'
}

if ($scriptFailed) {
    exit 1
}

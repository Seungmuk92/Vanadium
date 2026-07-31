param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    # Target platform(s) for the published images. Docker Desktop on Windows
    # builds for the host architecture (linux/amd64) by default, which silently
    # produces images that will not start on an ARM server. Pin it explicitly.
    # Pass "linux/amd64,linux/arm64" for a multi-arch manifest — that needs a
    # docker-container buildx builder (created here automatically) and runs the
    # non-native half under QEMU emulation, so it is considerably slower.
    [string]$Platform = "linux/amd64",

    # Publish Docker images only; skip creating/pushing the git tag.
    [switch]$NoGitTag,

    # Allow tagging even when the working tree has uncommitted changes.
    [switch]$AllowDirty,

    # Do not move ':latest' onto this version. Use when re-publishing an older
    # line (e.g. a 0.1.x hotfix after 0.2.0 shipped) so ':latest' is not rolled
    # backwards for everyone deploying without a pinned VANADIUM_VERSION.
    [switch]$NoLatest,

    # Skip the pre-publish test run.
    [switch]$SkipTests,

    # Skip the Docker Hub login pre-check.
    [switch]$SkipLoginCheck
)

$ErrorActionPreference = 'Stop'

$DockerHubUser = "smoh92"
$GitRemote     = "origin"
$GitTag        = "v$Version"
$Solution      = "Vanadium.slnx"

$Images = @(
    @{ Name = "$DockerHubUser/vanadium-rest"; Context = "Vanadium.Note.REST" },
    @{ Name = "$DockerHubUser/vanadium-web";  Context = "Vanadium.Note.Web"  }
)

# --- Pre-flight git checks (run BEFORE any Docker work so we fail fast) ---
if (-not $NoGitTag) {
    git rev-parse --is-inside-work-tree *> $null
    if ($LASTEXITCODE -ne 0) { throw "Not inside a git repository. Use -NoGitTag to publish without tagging." }

    # The tag must not already exist locally or on the remote.
    if (git tag --list $GitTag) {
        throw "Git tag '$GitTag' already exists locally. Bump the version or delete the tag first."
    }
    if (git ls-remote --tags $GitRemote "refs/tags/$GitTag") {
        throw "Git tag '$GitTag' already exists on '$GitRemote'."
    }

    # Refuse to tag a dirty working tree unless explicitly allowed, so the tag
    # always points at a reproducible, committed state.
    if (-not $AllowDirty -and (git status --porcelain)) {
        throw "Working tree has uncommitted changes. Commit them or pass -AllowDirty."
    }
}

# --- Pre-flight Docker Hub login check ---
# Without this the script builds every image first and only discovers the missing
# credentials at the push step, throwing away the whole build.
if (-not $SkipLoginCheck) {
    $dockerConfig = Join-Path $env:USERPROFILE ".docker\config.json"
    $loggedIn = $false
    if (Test-Path $dockerConfig) {
        $cfg = Get-Content $dockerConfig -Raw | ConvertFrom-Json
        if ($cfg.PSObject.Properties.Name -contains 'auths') {
            # `docker login` writes this key for Docker Hub whether the token is
            # stored inline or delegated to a credential helper.
            $loggedIn = @($cfg.auths.PSObject.Properties.Name |
                Where-Object { $_ -match 'index\.docker\.io' }).Count -gt 0
        }
    }
    if (-not $loggedIn) {
        throw "Not logged in to Docker Hub (no index.docker.io entry in $dockerConfig). Run 'docker login' first, or pass -SkipLoginCheck."
    }
}

# --- Pre-flight test run ---
# The E2E project self-ignores unless VANADIUM_E2E_BASEURL is set, so this is
# the unit + smoke pass only and needs no browsers or running stack.
if (-not $SkipTests) {
    Write-Host "`n==> Running tests" -ForegroundColor Cyan
    dotnet test $Solution --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Tests failed. Fix them or pass -SkipTests to publish anyway." }
}

# --- Provenance metadata baked into the images as OCI labels ---
$revision = (git rev-parse HEAD 2>$null)
if ($LASTEXITCODE -ne 0) { $revision = "" }
$source = (git remote get-url $GitRemote 2>$null)
if ($LASTEXITCODE -ne 0) { $source = "" }

# --- buildx builder ---
# The default "docker" driver cannot emit a multi-platform manifest; a
# docker-container builder can. Single-platform builds work on either.
$builderArgs = @()
if ($Platform.Contains(',')) {
    $builderName = "vanadium-builder"
    docker buildx inspect $builderName *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "==> Creating buildx builder '$builderName' (multi-platform)" -ForegroundColor Cyan
        docker buildx create --name $builderName --driver docker-container *> $null
        if ($LASTEXITCODE -ne 0) { throw "Failed to create buildx builder '$builderName'." }
    }
    $builderArgs = @('--builder', $builderName)
}

# --- Build & push Docker images ---
foreach ($img in $Images) {
    $name    = $img.Name
    $context = $img.Context

    $tagArgs = @('--tag', "${name}:${Version}")
    if (-not $NoLatest) { $tagArgs += @('--tag', "${name}:latest") }

    $labelArgs = @(
        '--label', "org.opencontainers.image.version=$Version"
        '--label', "org.opencontainers.image.title=$name"
    )
    if ($revision) { $labelArgs += @('--label', "org.opencontainers.image.revision=$revision") }
    if ($source)   { $labelArgs += @('--label', "org.opencontainers.image.source=$source") }

    Write-Host "`n==> Building & pushing ${name}:${Version} ($Platform)" -ForegroundColor Cyan
    # --push uploads straight from the build, so there is no separate push step
    # that could succeed for one tag and fail for the other.
    # --provenance=false keeps Docker Hub's tag list free of the "unknown/unknown"
    # attestation entries buildx attaches by default.
    docker buildx build @builderArgs `
        --platform $Platform `
        @tagArgs `
        @labelArgs `
        --provenance=false `
        --push `
        $context
    if ($LASTEXITCODE -ne 0) { throw "Build/push failed for $name" }
}

Write-Host "`nDone. Published version $Version." -ForegroundColor Green

# --- Tag & push (reached only if every Docker push above succeeded) ---
if (-not $NoGitTag) {
    Write-Host "`n==> Tagging $GitTag" -ForegroundColor Cyan
    git tag -a $GitTag -m "Release $GitTag"
    if ($LASTEXITCODE -ne 0) { throw "Failed to create git tag $GitTag" }

    Write-Host "==> Pushing tag $GitTag to $GitRemote" -ForegroundColor Cyan
    git push $GitRemote $GitTag
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to push git tag $GitTag. The local tag exists; run 'git push $GitRemote $GitTag' manually to retry."
    }

    Write-Host "Tagged and pushed $GitTag." -ForegroundColor Green
}

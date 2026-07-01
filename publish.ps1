param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    # Publish Docker images only; skip creating/pushing the git tag.
    [switch]$NoGitTag,

    # Allow tagging even when the working tree has uncommitted changes.
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'

$DockerHubUser = "smoh92"
$GitRemote     = "origin"
$GitTag        = "v$Version"

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

# --- Build & push Docker images ---
foreach ($img in $Images) {
    $name    = $img.Name
    $context = $img.Context
    $tagVer  = "${name}:${Version}"
    $tagLatest = "${name}:latest"

    Write-Host "`n==> Building $tagVer" -ForegroundColor Cyan
    docker build -t $tagVer -t $tagLatest $context
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $name" }

    Write-Host "==> Pushing $tagVer" -ForegroundColor Cyan
    docker push $tagVer
    if ($LASTEXITCODE -ne 0) { throw "Push failed for $tagVer" }

    Write-Host "==> Pushing $tagLatest" -ForegroundColor Cyan
    docker push $tagLatest
    if ($LASTEXITCODE -ne 0) { throw "Push failed for $tagLatest" }
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

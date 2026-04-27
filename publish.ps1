param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$DockerHubUser = "smoh92"
$Images = @(
    @{ Name = "$DockerHubUser/vanadium-rest"; Context = "Vanadium.Note.REST" },
    @{ Name = "$DockerHubUser/vanadium-web";  Context = "Vanadium.Note.Web"  }
)

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

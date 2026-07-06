<#
  create-issues.ps1  —  Seungmuk92/Vanadium 이슈 일괄 생성
  소스: vanadium-issues.csv  (Create(Y) 칸에 Y 표시한 행만 생성)
  매핑: Label→--label, Milestone→--milestone, Title→제목 그대로, Body→본문(Problem+Fix)

  전제: gh 설치 + 인증 (gh auth login)
  사용:
    미리보기:  .\create-issues.ps1
    실제생성:  .\create-issues.ps1 -Execute
    셋업만:    .\create-issues.ps1 -SetupOnly -Execute
#>
param(
  [switch]$Execute,
  [switch]$SetupOnly,
  [string]$Repo = "Seungmuk92/Vanadium",
  [string]$Csv  = "$PSScriptRoot\vanadium-issues.csv"
)
$ErrorActionPreference = "Stop"

gh auth status 1>$null 2>$null
if ($LASTEXITCODE -ne 0) { Write-Error "gh 인증 필요: 'gh auth login'"; exit 1 }
Write-Host "Repo: $Repo" -ForegroundColor Cyan
if (-not $Execute) { Write-Host "*** DRY-RUN (실제 생성 안 함). 실제 실행은 -Execute ***`n" -ForegroundColor Yellow }

if (-not (Test-Path $Csv)) { Write-Error "CSV 없음: $Csv"; exit 1 }
$items = Import-Csv -Path $Csv

# 1) 라벨 생성/갱신 (CSV의 Label 값 전부)
Write-Host "라벨 확인/생성..." -ForegroundColor Cyan
$colors = @{ "bug"="d73a4a"; "enhancement"="0e8a16" }
foreach ($lab in ($items.Label | Where-Object { $_ } | Sort-Object -Unique)) {
  $c = if ($colors.ContainsKey($lab)) { $colors[$lab] } else { "ededed" }
  if ($Execute) { gh label create $lab --repo $Repo --color $c --force 1>$null; Write-Host "  + label: $lab" -ForegroundColor Green }
  else { Write-Host "  [preview] label: $lab" -ForegroundColor DarkGray }
}

# 2) 마일스톤 생성 (CSV의 Milestone 값 중 없는 것만)
Write-Host "마일스톤 확인/생성..." -ForegroundColor Cyan
$existing = gh api "repos/$Repo/milestones?state=all" --jq ".[].title" 2>$null
foreach ($m in ($items.Milestone | Where-Object { $_ } | Sort-Object -Unique)) {
  if ($existing -contains $m) { Write-Host "  = $m (이미 있음)" -ForegroundColor DarkGray }
  elseif ($Execute) { gh api --method POST "repos/$Repo/milestones" -f title="$m" 1>$null; Write-Host "  + milestone: $m" -ForegroundColor Green }
  else { Write-Host "  [preview] milestone: $m" -ForegroundColor DarkGray }
}

if ($SetupOnly) { Write-Host "`nSetupOnly 완료." -ForegroundColor Cyan; exit 0 }

# 3) Create(Y)=Y 행만 이슈 생성
$targets = $items | Where-Object { $_.'Create(Y)'.Trim().ToUpper() -eq 'Y' }
Write-Host "`n생성 대상: $($targets.Count) 건 (전체 $($items.Count) 중)`n" -ForegroundColor Cyan
if ($targets.Count -eq 0) { Write-Host "Create(Y) 칸에 Y 표시된 행이 없습니다." -ForegroundColor Yellow; exit 0 }

$created = 0
foreach ($it in $targets) {
  $args = @("issue","create","--repo",$Repo,"--title",$it.Title,"--body",$it.Body)
  if ($it.Label -and $it.Label.Trim())         { $args += @("--label",     $it.Label.Trim()) }
  if ($it.Milestone -and $it.Milestone.Trim()) { $args += @("--milestone", $it.Milestone.Trim()) }
  if ($Execute) {
    Write-Host "  + $($it.Title)" -ForegroundColor Green
    $url = gh @args; Write-Host "     $url" -ForegroundColor DarkGray; $created++
  } else {
    Write-Host "  [preview] $($it.Title)  | label=$($it.Label) milestone=$($it.Milestone)" -ForegroundColor DarkGray
  }
}
if ($Execute) { Write-Host "`n완료: $created 건 생성." -ForegroundColor Cyan }
else { Write-Host "`n미리보기 종료. 실제 생성은 -Execute." -ForegroundColor Yellow }

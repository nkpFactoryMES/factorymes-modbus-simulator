# 고정 공개 릴리스 자산을 SHA-256으로 검증한다. 자격 증명은 필요 없다.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$lockPath = Join-Path $PSScriptRoot 'package-lock.json'
$manifest = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$packageDirectory = Join-Path $repoRoot '.packages'
New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
foreach ($package in $manifest.packages) {
  if ($package.sha256 -notmatch '^[a-fA-F0-9]{64}$') {
    throw "Missing or invalid package hash: $($package.id)"
  }
  $target = Join-Path $packageDirectory $package.file
  if (Test-Path -LiteralPath $target) {
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $package.sha256) {
      continue
    }
    throw "Existing package checksum mismatch: $($package.id)"
  }
  $download = $target + '.download'
  $url = 'https://github.com/nkpFactoryMES/' + $package.repo + '/releases/download/' `
    + $package.tag + '/' + $package.file
  try {
    Invoke-WebRequest -Uri $url -OutFile $download
    if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $package.sha256) {
      throw "Downloaded package checksum mismatch: $($package.id)"
    }
    Move-Item -LiteralPath $download -Destination $target
  }
  finally {
    if (Test-Path -LiteralPath $download) {
      Remove-Item -LiteralPath $download
    }
  }
}

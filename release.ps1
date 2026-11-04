$ErrorActionPreference = 'Stop'
$dist = Join-Path $PSScriptRoot 'dist'
[void][IO.Directory]::CreateDirectory($dist)
$executable = Join-Path $dist 'AgentScreenGuard.exe'
& (Join-Path $PSScriptRoot 'build.ps1') -OutputPath $executable
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion
$hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($executable + '.sha256', $hash + '  AgentScreenGuard.exe' + [Environment]::NewLine)
$zip = Join-Path $dist "AgentScreenGuard-v$version-win-x64.zip"
Compress-Archive -LiteralPath @($executable, (Join-Path $PSScriptRoot 'README.md')) -DestinationPath $zip -Force
Write-Output "배포 버전: $version"
Write-Output $zip

param([string]$OutputPath = (Join-Path $PSScriptRoot 'AgentScreenGuard.exe'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = $OutputPath
$icon = Join-Path $PSScriptRoot 'assets\app.ico'
if (-not (Test-Path -LiteralPath $icon)) { & (Join-Path $PSScriptRoot 'tools\create-icon.ps1') }
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($output)))
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/out:$output" "/win32icon:$icon" "/resource:$icon,AgentScreenGuard.AppIcon" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Security.dll /r:System.Core.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll @sources
if ($LASTEXITCODE -ne 0) { throw '빌드 실패' }
Write-Output $output

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $PSScriptRoot 'SmokeTest.exe'
$icon = Join-Path $PSScriptRoot '..\assets\app.ico'
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output /main:AgentScreenGuard.SmokeTest "/out:$output" "/resource:$icon,AgentScreenGuard.AppIcon" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Security.dll /r:System.Core.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll @sources (Join-Path $PSScriptRoot 'SmokeTest.cs')
if ($LASTEXITCODE -ne 0) { throw '검증 도구 빌드 실패' }
Write-Output $output

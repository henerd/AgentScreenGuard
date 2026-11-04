$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sandbox = Join-Path $PSScriptRoot 'update-sandbox'
$stageRoot = Join-Path $env:LOCALAPPDATA 'AgentScreenGuard\updates'
[void][IO.Directory]::CreateDirectory($sandbox)
$old = Join-Path $sandbox 'old.exe'
$new = Join-Path $sandbox 'new.exe'
$bad = Join-Path $sandbox 'bad.exe'
& $compiler /nologo /target:winexe /platform:x64 "/out:$old" (Join-Path $PSScriptRoot 'UpdateProbe.cs')
if ($LASTEXITCODE -ne 0) { throw '이전 버전 검증 파일 빌드 실패' }
& $compiler /nologo /target:winexe /platform:x64 /define:NEW_VERSION "/out:$new" (Join-Path $PSScriptRoot 'UpdateProbe.cs')
if ($LASTEXITCODE -ne 0) { throw '새 버전 검증 파일 빌드 실패' }
& $compiler /nologo /target:winexe /platform:x64 /define:NEW_VERSION,BAD_START "/out:$bad" (Join-Path $PSScriptRoot 'UpdateProbe.cs')
if ($LASTEXITCODE -ne 0) { throw '시작 실패 검증 파일 빌드 실패' }
foreach ($case in @('success', 'rollback', 'tampered')) {
    $caseRoot = Join-Path $sandbox $case
    [void][IO.Directory]::CreateDirectory($caseRoot)
    $target = Join-Path $caseRoot 'AgentScreenGuard.exe'
    $marker = Join-Path $caseRoot 'started.txt'
    if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker }
    Copy-Item -LiteralPath $old -Destination $target -Force
    $stage = Join-Path $stageRoot ([guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($stage)
    $payload = Join-Path $stage 'payload.exe'
    Copy-Item -LiteralPath $(if ($case -eq 'rollback') { $bad } else { $new }) -Destination $payload
    $sha = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($case -eq 'tampered') { $sha = '0' * 64 }
    $parent = Start-Process -FilePath $target -ArgumentList '--wait' -WindowStyle Hidden -PassThru
    $plan = Join-Path $stage 'plan.json'
    @{ ParentProcessId = $parent.Id; TargetPath = $target; PayloadPath = $payload; Sha256 = $sha; Version = '1.0.1' } | ConvertTo-Json | Set-Content -LiteralPath $plan -Encoding utf8
    $installer = Start-Process -FilePath (Join-Path $PSScriptRoot 'SmokeTest.exe') -ArgumentList @('--update-install', ('"' + $plan + '"')) -WindowStyle Hidden -Wait -PassThru
    $expected = if ($case -eq 'success') { '1.0.1.0' } else { '1.0.0.0' }
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    while (-not (Test-Path -LiteralPath $marker) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if ([Reflection.AssemblyName]::GetAssemblyName($target).Version.ToString() -ne $expected) { throw "$case 파일 버전 검증 실패" }
    if ((Get-Content -LiteralPath $marker -Raw).Trim() -ne $expected) { throw "$case 재실행 검증 실패" }
    if ($case -eq 'success' -and $installer.ExitCode -ne 0) { throw '업데이트 성공 경로 실패' }
    if ($case -ne 'success' -and $installer.ExitCode -eq 0) { throw "$case 오류 감지 실패" }
    Write-Output "$case : PASS"
}

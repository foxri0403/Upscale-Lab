param(
    [ValidatePattern('^[A-Za-z0-9_.-]+@[A-Za-z0-9_.-]+$')][string]$RemoteHost,
    [ValidatePattern('^/[A-Za-z0-9_./-]+$')][string]$RemoteSettingsFile,
    [int]$WebPort = 5173,
    [int]$ApiPort = 5091,
    [switch]$Stop,
    [string]$RunDirectory
)
$ErrorActionPreference = 'Stop'
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function AssertExit($name) { if ($LASTEXITCODE -ne 0) { throw "$name failed" } }
function QuotedArguments([string[]]$values) {
    foreach ($value in $values) {
        if ($value.Contains('"') -or $value.Contains("`n") -or $value.Contains("`r") -or $value.EndsWith('\')) { throw 'Unsupported process argument' }
        '"' + $value + '"'
    }
}
if ($Stop) {
    if (-not $RunDirectory) { throw 'Pass the RunDirectory printed when the lab started.' }
    $resolved = [System.IO.Path]::GetFullPath($RunDirectory)
    $allowed = [System.IO.Path]::GetFullPath((Join-Path $repo 'artifacts')) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowed, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'RunDirectory must be inside this workspace artifacts folder.' }
    $state = Get-Content -LiteralPath (Join-Path $resolved 'running.json') -Raw | ConvertFrom-Json
    & ssh -o BatchMode=yes $state.remoteHost "sudo -n python3 '$($state.runner)' stop '$($state.id)' --settings '$($state.settings)' --port $($state.apiPort)"
    AssertExit 'Remote lab stop'
    foreach ($item in @($state.webProcess, $state.sshProcess)) {
        $process = Get-Process -Id $item.id -ErrorAction SilentlyContinue
        if ($process -and $process.StartTime.ToUniversalTime().Ticks -eq $item.startedTicks) { Stop-Process -Id $item.id }
    }
    Write-Output '분리 실험실과 이 실행에서 만든 테스트 파일을 정리했습니다.'
    return
}
if (-not $RemoteHost -or -not $RemoteSettingsFile) { throw 'Specify RemoteHost and RemoteSettingsFile. Provider secrets are read only on that host.' }
foreach ($port in @($WebPort, $ApiPort)) {
    if ($port -lt 1024 -or $port -gt 65535 -or (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)) { throw "Port $port is unavailable." }
}
$id = 'separation-lab-' + [DateTime]::UtcNow.AddHours(9).ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$run = Join-Path $repo "artifacts/$id"
New-Item -ItemType Directory -Path $run | Out-Null
$dotnet = Join-Path $repo '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet).Source }
$env:DOTNET_CLI_HOME = Join-Path $repo '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $repo '.nuget/packages'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnet publish (Join-Path $repo 'backend/tools/UpscaleLab.SeparationLab/UpscaleLab.SeparationLab.csproj') -c Release -r linux-x64 --self-contained false -p:UseAppHost=false -m:1 -nodeReuse:false -o (Join-Path $run 'publish')
AssertExit 'Lab publish'
& tar -czf (Join-Path $run "$id.tar.gz") -C (Join-Path $run 'publish') .
AssertExit 'Lab package'
$sha = (Get-FileHash -LiteralPath (Join-Path $run "$id.tar.gz") -Algorithm SHA256).Hash.ToLowerInvariant()
$remoteHome = (& ssh -o BatchMode=yes $RemoteHost "python3 -c 'import pathlib;print(pathlib.Path.home())'").Trim()
AssertExit 'Remote home read'
if ($remoteHome -notmatch '^/[A-Za-z0-9_./-]+$') { throw 'Unexpected remote home path' }
$remoteBase = "$remoteHome/upscale-separation-labs"
& ssh -o BatchMode=yes $RemoteHost "mkdir -p '$remoteBase'"
AssertExit 'Lab directory creation'
& scp (Join-Path $run "$id.tar.gz") "${RemoteHost}:$remoteBase/$id.tar.gz"
AssertExit 'Lab upload'
$runner = "$remoteBase/$id-runner.py"
& scp (Join-Path $repo 'backend/tools/UpscaleLab.SeparationLab/remote_runner.py') "${RemoteHost}:$runner"
AssertExit 'Lab runner upload'
& ssh -o BatchMode=yes $RemoteHost "python3 '$runner' stage '$id' --settings '$RemoteSettingsFile' --port $ApiPort --sha256 $sha"
AssertExit 'Lab staging'
$sshArgs = @('-o','BatchMode=yes','-o','ExitOnForwardFailure=yes','-L',"127.0.0.1:${ApiPort}:127.0.0.1:${ApiPort}",$RemoteHost,"sudo -n python3 '$runner' run '$id' --settings '$RemoteSettingsFile' --port $ApiPort")
$sshProcess = Start-Process -FilePath (Get-Command ssh).Source -ArgumentList ((QuotedArguments $sshArgs) -join ' ') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'api.log') -RedirectStandardError (Join-Path $run 'api-error.log')
$env:SEPARATION_LAB_API = "http://127.0.0.1:$ApiPort"
$env:VITE_APP_MODE = 'demo'
$vite = Join-Path $repo 'web/node_modules/vite/bin/vite.js'
$webProcess = Start-Process -FilePath (Get-Command node).Source -ArgumentList ((QuotedArguments @($vite,'--host','127.0.0.1','--port',"$WebPort",'--strictPort')) -join ' ') -WorkingDirectory (Join-Path $repo 'web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'web.log') -RedirectStandardError (Join-Path $run 'web-error.log')
$state = @{ id=$id; remoteHost=$RemoteHost; settings=$RemoteSettingsFile; runner=$runner; apiPort=$ApiPort; url="http://127.0.0.1:$WebPort/lab/separation";
    webProcess=@{id=$webProcess.Id;startedTicks=$webProcess.StartTime.ToUniversalTime().Ticks}; sshProcess=@{id=$sshProcess.Id;startedTicks=$sshProcess.StartTime.ToUniversalTime().Ticks} }
$state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'running.json')
$ready = $false
for ($attempt=0; $attempt -lt 45; $attempt++) {
    if ($sshProcess.HasExited -or $webProcess.HasExited) { break }
    try { $health = Invoke-RestMethod "http://127.0.0.1:$WebPort/__separation-lab/health" -TimeoutSec 2; if ($health.ready -and $health.isolated) { $ready=$true; break } } catch {}
    Start-Sleep -Seconds 1
}
if (-not $ready) { throw "Lab did not become ready. Inspect logs in $run; use the Stop command with this RunDirectory to clean up." }
Write-Output "URL: $($state.url)"
Write-Output "RunDirectory: $run"
Write-Output "Stop: ./web/scripts/separation-lab.ps1 -Stop -RunDirectory '$run'"

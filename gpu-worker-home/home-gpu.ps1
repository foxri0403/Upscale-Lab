param(
    [Parameter(Mandatory)]
    [ValidateSet('Install', 'Worker', 'Check', 'Api')]
    [string]$Mode,
    [switch]$WithLayers,
    [switch]$WithCharacter,
    [string]$DotNet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$python = Join-Path $PSScriptRoot '.venv/Scripts/python.exe'
$keyFile = Join-Path $PSScriptRoot 'home-gpu.key'
$workerUrl = 'http://127.0.0.1:18189'

if ($Mode -eq 'Install') {
    if (-not (Test-Path -LiteralPath $python)) {
        python -m venv (Join-Path $PSScriptRoot '.venv')
        if ($LASTEXITCODE -ne 0) { throw 'Python virtual environment creation failed.' }
    }
    & $python -m pip install torch==2.8.0+cu128 torchvision==0.23.0+cu128 --index-url https://download.pytorch.org/whl/cu128
    if ($LASTEXITCODE -ne 0) { throw 'CUDA-enabled PyTorch installation failed.' }
    & $python -m pip install wheel==0.45.1 Cython==3.2.4
    if ($LASTEXITCODE -ne 0) { throw 'Python wheel build support installation failed.' }
    $requirements = if ($WithLayers) { 'requirements-layers.txt' } else { 'requirements.txt' }
    # BasicSR's source metadata tries to install a second PyTorch in an isolated build.
    & $python -m pip install --no-build-isolation -r (Join-Path $PSScriptRoot $requirements)
    if ($LASTEXITCODE -ne 0) { throw 'GPU worker dependency installation failed.' }
    if ($WithCharacter) {
        & $python -m pip install 'psd-tools[composite]==1.14.2'
        if ($LASTEXITCODE -ne 0) { throw 'See-through ZIP export dependency installation failed.' }
    }
    return
}

if ($Mode -eq 'Worker') {
    if (-not (Test-Path -LiteralPath $python)) { throw 'Run home-gpu.ps1 -Mode Install first.' }
    if (-not (Test-Path -LiteralPath $keyFile)) {
        $bytes = [byte[]]::new(32)
        $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
        [IO.File]::WriteAllText($keyFile, [Convert]::ToBase64String($bytes), [Text.Encoding]::ASCII)
    }
    $serverArguments = @(Join-Path $PSScriptRoot 'server.py')
    if ($WithLayers) { $serverArguments += '--layers' }
    if ($WithCharacter) {
        $serverArguments += @('--character-root', (Join-Path $PSScriptRoot '../tmp/see-through'),
            '--character-models', (Join-Path $PSScriptRoot '../tmp/see-through-models'))
    }
    & $python -X utf8 -u @serverArguments
    if ($LASTEXITCODE -ne 0) { throw 'Home GPU worker exited with an error.' }
    return
}

if (-not (Test-Path -LiteralPath $keyFile)) { throw 'Start home-gpu.ps1 -Mode Worker first.' }
$key = (Get-Content -LiteralPath $keyFile -Raw).Trim()
$health = Invoke-RestMethod -Uri "$workerUrl/health" -Headers @{ Authorization = "Bearer $key" } -TimeoutSec 5
if ($health.status -ne 'ready' -or $health.endpoints -notcontains 'local-upscale') {
    throw 'Home GPU worker is not ready.'
}
if ($Mode -eq 'Check') {
    $health
    return
}

$settings = @{
    ASPNETCORE_ENVIRONMENT = 'Development'
    DOTNET_ENVIRONMENT = 'Development'
    Processing__Provider = 'LocalGpu'
    LocalGpu__ApiBaseUrl = "$workerUrl/v2/"
    LocalGpu__ApiKey = $key
    LocalGpu__UpscaleEnabled = 'true'
    LocalGpu__LayersEnabled = ($health.endpoints -contains 'local-layers').ToString().ToLowerInvariant()
    SeeThrough__Enabled = ($health.endpoints -contains 'local-character').ToString().ToLowerInvariant()
    SeeThrough__Provider = 'LocalGpu'
    SeeThrough__LocalGpuApiBaseUrl = "$workerUrl/v2/"
    SeeThrough__LocalGpuApiKey = $key
}
$previous = @{}
foreach ($name in $settings.Keys) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process')
}
try {
    $project = Join-Path $PSScriptRoot '../backend/src/UpscaleLab.Api'
    & $DotNet build $project --nologo -m:1
    if ($LASTEXITCODE -ne 0) { throw 'The development API could not be built.' }
    & $DotNet run --project $project --no-build --no-launch-profile -- --urls 'http://127.0.0.1:5080'
    if ($LASTEXITCODE -ne 0) { throw 'The development API exited with an error.' }
}
finally {
    foreach ($name in $previous.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
}

param(
    [Parameter(Mandatory)][string]$ApiBaseUrl,
    [string]$Flutter = 'flutter',
    [string]$Dotnet = 'dotnet',
    [string]$ApkSigner = 'apksigner'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$uri = [Uri]$ApiBaseUrl
if (!$uri.IsAbsoluteUri -or $uri.Scheme -notin @('https','http') -or $uri.UserInfo -or $uri.AbsolutePath -ne '/' -or $uri.Query -or $uri.Fragment) { throw 'Supply an HTTP(S) server origin only.' }
if (!$env:UPSCALE_SIGNING_PROPERTIES_FILE -or !(Test-Path -LiteralPath $env:UPSCALE_SIGNING_PROPERTIES_FILE)) { throw 'Set UPSCALE_SIGNING_PROPERTIES_FILE to the external team signing properties.' }
$version = [regex]::Match([IO.File]::ReadAllText((Join-Path $repo 'mobile/pubspec.yaml')), '(?m)^version: (\d+\.\d+\.\d+)\+(\d+)').Groups[1].Value
if (!$version) { throw 'APK version was not found.' }
$release = Join-Path $repo 'web/public/downloads'
New-Item -ItemType Directory -Force -Path $release | Out-Null
& $Dotnet publish (Join-Path $PSScriptRoot 'windows/UpscaleLab.Companion.Windows.csproj') -c Release -r win-x64 --self-contained true "-p:CompanionDefaultApiBaseUrl=$ApiBaseUrl" "-p:Version=$version" -p:DebugType=None -o (Join-Path $repo 'artifacts/windows')
if ($LASTEXITCODE) { throw 'Windows publish failed.' }
$previousHttp = $env:ORG_GRADLE_PROJECT_companionAllowHttp
Push-Location (Join-Path $repo 'mobile')
try {
    $env:ORG_GRADLE_PROJECT_companionAllowHttp = ($uri.Scheme -eq 'http').ToString().ToLowerInvariant()
    & $Flutter build apk --release "--dart-define=API_BASE_URL=$ApiBaseUrl"
    if ($LASTEXITCODE) { throw 'Android build failed.' }
} finally { Pop-Location; $env:ORG_GRADLE_PROJECT_companionAllowHttp = $previousHttp }
$apk = Join-Path $repo 'mobile/build/app/outputs/flutter-apk/app-release.apk'
& $ApkSigner verify --print-certs $apk
if ($LASTEXITCODE) { throw 'APK signature verification failed.' }
$windowsName = "UpscaleLab-Connect-$version-win-x64.exe"
$androidName = "Upscale_Lab-$version-.apk"
Copy-Item -LiteralPath (Join-Path $repo 'artifacts/windows/UpscaleLab.Connect.exe') -Destination (Join-Path $release $windowsName)
Copy-Item -LiteralPath $apk -Destination (Join-Path $repo "artifacts/$androidName")
Copy-Item -LiteralPath $apk -Destination (Join-Path $release $androidName)
$items = @(@{ platform='Windows'; name=$windowsName }, @{ platform='Android'; name=$androidName }) | ForEach-Object {
    $path = Join-Path $release $_.name
    @{ platform=$_.platform; version=$version; url="/downloads/$($_.name)"; sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant(); sizeBytes=(Get-Item -LiteralPath $path).Length }
}
@{ schemaVersion=1; publishedAt=[DateTime]::UtcNow.ToString('o'); releases=@($items) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $release 'manifest.json') -Encoding utf8NoBOM
Write-Output 'Release binaries and checksum manifest are ready in web/public/downloads. Build and deploy the web UI manually.'

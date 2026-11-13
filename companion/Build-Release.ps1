param(
    [string]$ApiBaseUrl,
    [string]$AndroidApiBaseUrl,
    [switch]$LocalTest,
    [string]$Flutter = 'flutter',
    [string]$Dotnet = 'dotnet',
    [string]$ApkSigner = 'apksigner'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
if ($LocalTest) {
    if (!$ApiBaseUrl) { $ApiBaseUrl = 'http://127.0.0.1:5080' }
    if (!$AndroidApiBaseUrl) { $AndroidApiBaseUrl = 'http://10.0.2.2:5080' }
}
if (!$AndroidApiBaseUrl) { $AndroidApiBaseUrl = $ApiBaseUrl }
foreach ($origin in @($ApiBaseUrl, $AndroidApiBaseUrl)) {
    if (!$origin) { throw 'Supply -ApiBaseUrl for release builds, or use -LocalTest.' }
    $uri = [Uri]$origin
    if (!$uri.IsAbsoluteUri -or $uri.Scheme -notin @('https','http') -or !$uri.Host -or $uri.UserInfo -or $uri.AbsolutePath -ne '/' -or $uri.Query -or $uri.Fragment) { throw 'Supply an HTTP(S) server origin only.' }
}
if (!$env:UPSCALE_SIGNING_PROPERTIES_FILE -or !(Test-Path -LiteralPath $env:UPSCALE_SIGNING_PROPERTIES_FILE)) { throw 'Set UPSCALE_SIGNING_PROPERTIES_FILE to the external team signing properties.' }
$androidVersion = [regex]::Match([IO.File]::ReadAllText((Join-Path $repo 'mobile/pubspec.yaml')), '(?m)^version: (\d+\.\d+\.\d+)\+(\d+)').Groups[1].Value
if (!$androidVersion) { throw 'APK version was not found.' }
$windowsProject = Join-Path $PSScriptRoot 'windows/UpscaleLab.Companion.Windows.csproj'
$windowsVersion = ([xml][IO.File]::ReadAllText($windowsProject)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($windowsVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Windows app version was not found.' }
$release = if ($LocalTest) { Join-Path $repo 'artifacts/companion-local' } else { Join-Path $repo 'web/public/downloads' }
$windowsOutput = Join-Path $repo $(if ($LocalTest) { 'artifacts/windows-local' } else { 'artifacts/windows' })
New-Item -ItemType Directory -Force -Path $release | Out-Null
& $Dotnet publish $windowsProject -c Release -r win-x64 --self-contained true "-p:CompanionDefaultApiBaseUrl=$ApiBaseUrl" "-p:CompanionLocalTest=$($LocalTest.ToString().ToLowerInvariant())" -p:DebugType=None -o $windowsOutput
if ($LASTEXITCODE) { throw 'Windows publish failed.' }
Push-Location (Join-Path $repo 'mobile')
try {
    $allowHttp = (([Uri]$AndroidApiBaseUrl).Scheme -eq 'http').ToString().ToLowerInvariant()
    & $Flutter build apk --release "--dart-define=API_BASE_URL=$AndroidApiBaseUrl" "--dart-define=COMPANION_LOCAL_TEST=$($LocalTest.ToString().ToLowerInvariant())" "--dart-define=COMPANION_ALLOW_HTTP=$allowHttp"
    if ($LASTEXITCODE) { throw 'Android build failed.' }
} finally { Pop-Location }
$apk = Join-Path $repo 'mobile/build/app/outputs/flutter-apk/app-release.apk'
& $ApkSigner verify --print-certs $apk
if ($LASTEXITCODE) { throw 'APK signature verification failed.' }
$aapt = Join-Path (Split-Path (Get-Command $ApkSigner).Source) 'aapt2.exe'
if (Test-Path -LiteralPath $aapt) {
    $badging = & $aapt dump badging $apk
    if ($LASTEXITCODE) { throw 'APK metadata verification failed.' }
    $expectedPackage = if ($LocalTest) { 'com.livelayer.app.localtest' } else { 'com.livelayer.app' }
    if (!($badging | Select-String -SimpleMatch "package: name='$expectedPackage'")) { throw 'APK package ID does not match the requested build mode.' }
    if (!($badging | Select-String -SimpleMatch "versionName='$androidVersion'")) { throw 'APK version does not match pubspec.yaml.' }
}
$windowsName = if ($LocalTest) { "UpscaleLab-Connect-$windowsVersion-local-win-x64.exe" } else { "UpscaleLab-Connect-$windowsVersion-win-x64.exe" }
$androidName = if ($LocalTest) { "Upscale_Lab-$androidVersion-local.apk" } else { "Upscale_Lab-$androidVersion-.apk" }
Copy-Item -LiteralPath (Join-Path $windowsOutput 'UpscaleLab.Connect.exe') -Destination (Join-Path $release $windowsName)
Copy-Item -LiteralPath $apk -Destination (Join-Path $release $androidName)
if (!$LocalTest) { Copy-Item -LiteralPath $apk -Destination (Join-Path $repo "artifacts/$androidName") }
$items = @(@{ platform='Windows'; name=$windowsName; version=$windowsVersion }, @{ platform='Android'; name=$androidName; version=$androidVersion }) | ForEach-Object {
    $path = Join-Path $release $_.name
    @{ platform=$_.platform; version=$_.version; url="/downloads/$($_.name)"; sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant(); sizeBytes=(Get-Item -LiteralPath $path).Length }
}
@{ schemaVersion=1; publishedAt=[DateTime]::UtcNow.ToString('o'); localTest=[bool]$LocalTest; releases=@($items) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $release 'manifest.json') -Encoding utf8NoBOM
Write-Output "Verified binaries are ready in $release. No deployment was performed."

param(
    [switch]$WithLayers,
    [switch]$WithCharacter
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not ($WithLayers -or $WithCharacter)) { throw 'Select -WithLayers, -WithCharacter, or both.' }

function Get-ModelFile([string]$Url, [string]$Path, [string]$Sha256) {
    if (Test-Path -LiteralPath $Path) {
        if ($Sha256 -and (Get-FileHash -LiteralPath $Path).Hash -ne $Sha256) {
            throw "Existing file checksum mismatch: $Path"
        }
        return
    }
    New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
    $download = "$Path.download"
    curl.exe --fail --location --silent --show-error --retry 3 $Url -o $download
    if ($LASTEXITCODE -ne 0) { throw "Model download failed: $Path" }
    if ($Sha256 -and (Get-FileHash -LiteralPath $download).Hash -ne $Sha256) {
        throw "Downloaded model checksum mismatch: $Path"
    }
    Move-Item -LiteralPath $download -Destination $Path
}

function Get-PinnedSource([string]$Url, [string]$Path, [string]$Revision) {
    if (-not (Test-Path -LiteralPath $Path)) {
        git clone --filter=blob:none --no-checkout $Url $Path
        if ($LASTEXITCODE -ne 0) { throw "Could not clone $Url" }
        git -C $Path checkout --detach $Revision
        if ($LASTEXITCODE -ne 0) { throw "Could not check out $Revision" }
    }
    $head = git -C $Path rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $head -ne $Revision) { throw "Expected $Revision in $Path" }
}

function Get-HfSnapshot([string]$Model, [string]$Revision, [string]$Root) {
    $metadata = Invoke-RestMethod "https://huggingface.co/api/models/$Model/revision/${Revision}?blobs=true"
    foreach ($file in $metadata.siblings) {
        if ($file.rfilename -eq '.gitattributes') { continue }
        Get-ModelFile "https://huggingface.co/$Model/resolve/$Revision/$($file.rfilename)" `
            (Join-Path $Root $file.rfilename) $file.lfs.sha256
    }
}

if ($WithLayers) {
    $layerRoot = Join-Path $repoRoot 'gpu-worker-runpod/layers/models'
    $biRefUrl = 'https://huggingface.co/ZhengPeng7/BiRefNet_HR-matting/resolve/fecd89c4fca69964f82f9c40bc4428e78e06f448'
    foreach ($file in 'config.json','BiRefNet_config.py','birefnet.py') {
        Get-ModelFile "$biRefUrl/$file" (Join-Path $layerRoot "birefnet/$file")
    }
    Get-ModelFile "$biRefUrl/model.safetensors" (Join-Path $layerRoot 'birefnet/model.safetensors') `
        'a5a4de698739ea5e0e8bbab28e1b293dde95092b87a442d566cbc585c53cef55'
    $archive = Join-Path $layerRoot 'big-lama.zip'
    Get-ModelFile 'https://huggingface.co/smartywu/big-lama/resolve/05cb2be7f8dbe6ca7c6e78f4fc827a4b2baaa4a9/big-lama.zip' `
        $archive 'f1b358ca24093b93a106183b98a3dea6e8ed09f3b43ea7251eb2c81e7b4575f6'
    $checkpoint = Join-Path $layerRoot 'big-lama/models/best.ckpt'
    if (-not (Test-Path -LiteralPath $checkpoint)) {
        python -m zipfile -e $archive $layerRoot
        if ($LASTEXITCODE -ne 0) { throw 'LaMa archive could not be extracted.' }
    }
    if ((Get-FileHash -LiteralPath $checkpoint).Hash -ne 'fccb7adffd53ec0974ee5503c3731c2c2f1e7e07856fd9228cdcc0b46fd5d423') {
        throw 'LaMa checkpoint checksum mismatch.'
    }
    Get-PinnedSource 'https://github.com/advimman/lama.git' (Join-Path $layerRoot 'lama-source') `
        '786f5936b27fb3dacd2b1ad799e4de968ea697e7'
}

if ($WithCharacter) {
    $source = Join-Path $repoRoot 'tmp/see-through'
    New-Item -ItemType Directory -Force (Split-Path $source) | Out-Null
    Get-PinnedSource 'https://github.com/shitagaki-lab/see-through.git' $source 'fc4440db7c076518e313f378e28b1504e9886a95'
    $root = Join-Path $repoRoot 'tmp/see-through-models'
    Get-HfSnapshot '24yearsold/seethroughv0.0.2_layerdiff3d_nf4' '96f9311ff449dc86cfaba101ce4dfb4fd4958b80' (Join-Path $root 'layerdiff')
    Get-HfSnapshot '24yearsold/seethroughv0.0.1_marigold_nf4' 'e53b344b5aa5beec7c141bd5601decaaff0898c9' (Join-Path $root 'depth')

    $python = Join-Path $source '.venv/Scripts/python.exe'
    if (-not (Test-Path -LiteralPath $python)) {
        python -m venv (Join-Path $source '.venv')
        if ($LASTEXITCODE -ne 0) { throw 'See-through virtual environment creation failed.' }
    }
    Push-Location $source
    try {
        & $python -m pip install torch==2.8.0+cu128 torchvision==0.23.0+cu128 --index-url https://download.pytorch.org/whl/cu128
        if ($LASTEXITCODE -ne 0) { throw 'See-through CUDA-enabled PyTorch installation failed.' }
        $inferenceRequirements = Join-Path $repoRoot 'tmp/see-through-inference-requirements.txt'
        Get-Content requirements.txt -Encoding UTF8 | Where-Object { $_ -notmatch '^convnext_perceptual_loss @' } |
            Set-Content -LiteralPath $inferenceRequirements -Encoding UTF8
        & $python -X utf8 -m pip install -r $inferenceRequirements -r requirements-inference-bnb.txt
        if ($LASTEXITCODE -ne 0) { throw 'See-through dependencies could not be installed.' }
    }
    finally { Pop-Location }
}

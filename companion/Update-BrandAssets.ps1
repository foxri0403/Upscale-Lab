# Rasterize the existing web SVG without changing the brand artwork.
# Run with Windows PowerShell -STA; generated icon assets are checked in.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$repo = Split-Path $PSScriptRoot
[xml]$svg = Get-Content -LiteralPath (Join-Path $repo 'web/public/favicon.svg') -Raw
function Write-BrandPng([int]$size, [string]$path) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $drawing = $visual.RenderOpen()
    $drawing.DrawRectangle([System.Windows.Media.Brushes]::White, $null, (New-Object System.Windows.Rect 0,0,$size,$size))
    $scale = $size / 64.0
    $drawing.PushTransform((New-Object System.Windows.Media.TranslateTransform (12*$scale),(10*$scale)))
    $drawing.PushTransform((New-Object System.Windows.Media.ScaleTransform $scale,$scale))
    foreach ($pathElement in $svg.svg.path) {
        $brush = [System.Windows.Media.BrushConverter]::new().ConvertFromString($pathElement.fill)
        $drawing.DrawGeometry($brush, $null, [System.Windows.Media.Geometry]::Parse($pathElement.d))
    }
    $drawing.Pop(); $drawing.Pop(); $drawing.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size,$size,96,96,([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    $stream = [IO.File]::Create($path)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
}
$brand = Join-Path $PSScriptRoot 'assets'
Write-BrandPng 256 (Join-Path $brand 'app-icon.png')
$iconPath = Join-Path $brand 'app.ico'
$iconStream = [IO.File]::Create($iconPath)
$writer = [IO.BinaryWriter]::new($iconStream)
try {
    $png = [IO.File]::ReadAllBytes((Join-Path $brand 'app-icon.png'))
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
    $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$png.Length); $writer.Write([uint32]22)
    $writer.Write($png)
} finally { $writer.Dispose() }
Write-BrandPng 256 (Join-Path $repo 'mobile/assets/app-icon.png')
foreach ($density in @{ mdpi=48; hdpi=72; xhdpi=96; xxhdpi=144; xxxhdpi=192 }.GetEnumerator()) {
    Write-BrandPng $density.Value (Join-Path $repo "mobile/android/app/src/main/res/mipmap-$($density.Key)/ic_launcher.png")
}

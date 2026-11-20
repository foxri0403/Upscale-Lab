# Verify the published bundle and stage only the verified installer and its metadata.
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$ApiBaseUrl
)

$ErrorActionPreference = 'Stop'

function Read-BundleEntry([IO.BinaryReader]$Reader, [pscustomobject]$Entry) {
    $Reader.BaseStream.Position = $Entry.Offset
    $storedSize = if ($Entry.CompressedSize -gt 0) { $Entry.CompressedSize } else { $Entry.Size }
    $bytes = $Reader.ReadBytes([int]$storedSize)
    if ($bytes.Length -ne $storedSize) { throw "Truncated bundle entry $($Entry.Name)." }
    if ($Entry.CompressedSize -eq 0) { return ,$bytes }

    $source = [IO.MemoryStream]::new($bytes)
    $inflater = [IO.Compression.DeflateStream]::new($source, [IO.Compression.CompressionMode]::Decompress)
    $decoded = [IO.MemoryStream]::new([int]$Entry.Size)
    try {
        $inflater.CopyTo($decoded)
        if ($decoded.Length -ne $Entry.Size) { throw "Unexpected expanded size for $($Entry.Name)." }
        return ,($decoded.ToArray())
    } finally {
        $decoded.Dispose()
        $inflater.Dispose()
        $source.Dispose()
    }
}

$files = @(Get-ChildItem -LiteralPath $PublishDirectory -Force)
if ($files.Count -ne 1 -or $files[0].PSIsContainer -or $files[0].Name -cne 'UpscaleLab.Connect.exe') {
    throw 'Expected a standalone UpscaleLab.Connect.exe with no sidecar files.'
}
$exe = $files[0]
$versionInfo = $exe.VersionInfo
if ($versionInfo.FileVersion -cne "$Version.0" -or ($versionInfo.ProductVersion -split '\+', 2)[0] -cne $Version) {
    throw 'The installer version does not match the Windows project.'
}
$signature = Get-AuthenticodeSignature -LiteralPath $exe.FullName
if ($signature.Status -ne 'NotSigned') { throw "Expected an unsigned EXE, found $($signature.Status)." }

Add-Type -AssemblyName System.Reflection.Metadata
$encoding = [Text.Encoding]::Latin1
$requiredEntries = @(
    'UpscaleLab.Connect.dll', 'UpscaleLab.Connect.deps.json', 'UpscaleLab.Connect.runtimeconfig.json',
    'System.Private.CoreLib.dll', 'PresentationFramework.dll', 'System.Windows.Forms.dll',
    'Magick.NET-Q8-AnyCPU.dll', 'Magick.NET.Core.dll', 'Magick.Native-Q8-x64.dll'
)
$stream = $exe.OpenRead()
$reader = [IO.BinaryReader]::new($stream)
try {
    $pe = [Reflection.PortableExecutable.PEReader]::new($stream, [Reflection.PortableExecutable.PEStreamOptions]::LeaveOpen)
    try {
        $headers = $pe.PEHeaders
        if ($headers.CoffHeader.Machine -ne 'Amd64' -or $headers.PEHeader.Subsystem -ne 'WindowsGui') {
            throw 'Expected an x64 Windows GUI executable.'
        }

        # The .NET apphost stores the bundle header offset immediately before this marker.
        $marker = $encoding.GetString([Convert]::FromHexString('8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae'))
        $headerOffset = 0
        foreach ($section in $headers.SectionHeaders) {
            $stream.Position = $section.PointerToRawData
            $sectionBytes = $reader.ReadBytes($section.SizeOfRawData)
            $position = $encoding.GetString($sectionBytes).IndexOf($marker, [StringComparison]::Ordinal)
            if ($position -ge 8) {
                $headerOffset = [BitConverter]::ToInt64($sectionBytes, $position - 8)
                break
            }
        }
    } finally { $pe.Dispose() }
    if ($headerOffset -le 0 -or $headerOffset -gt $stream.Length - 12) { throw 'Missing or invalid .NET bundle header.' }

    $stream.Position = $headerOffset
    if ($reader.ReadUInt32() -ne 6 -or $reader.ReadUInt32() -ne 0) { throw 'Unsupported .NET bundle format.' }
    $fileCount = $reader.ReadInt32()
    [void]$reader.ReadString() # Bundle identifier
    [void]$stream.Seek(40, [IO.SeekOrigin]::Current) # Dependency/runtime config ranges and bundle flags
    if ($fileCount -lt $requiredEntries.Count -or $fileCount -gt ($stream.Length - $stream.Position) / 26) {
        throw 'Invalid bundle entry count.'
    }

    $entries = @{}
    for ($i = 0; $i -lt $fileCount; $i++) {
        $offset = $reader.ReadInt64()
        $size = $reader.ReadInt64()
        $compressedSize = $reader.ReadInt64()
        [void]$reader.ReadByte() # File type
        $name = $reader.ReadString()
        $storedSize = if ($compressedSize -gt 0) { $compressedSize } else { $size }
        if (!$name -or $entries.ContainsKey($name) -or $size -lt 0 -or $size -gt [int]::MaxValue -or
            $compressedSize -lt 0 -or $compressedSize -gt [int]::MaxValue -or
            $offset -le 0 -or $storedSize -gt $headerOffset - $offset) {
            throw "Invalid bundle entry '$name'."
        }
        $entries.Add($name, [pscustomobject]@{ Name = $name; Offset = $offset; Size = $size; CompressedSize = $compressedSize })
    }
    foreach ($name in $requiredEntries) {
        if (!$entries.ContainsKey($name)) { throw "Missing required bundle entry '$name'." }
    }

    $payload = @{}
    foreach ($entry in $entries.Values) {
        $bytes = Read-BundleEntry $reader $entry
        if ($requiredEntries[0..2] -contains $entry.Name) { $payload.Add($entry.Name, $bytes) }
    }
} finally { $reader.Dispose() }

$deps = [Text.Encoding]::UTF8.GetString($payload['UpscaleLab.Connect.deps.json']) | ConvertFrom-Json
$runtime = [Text.Encoding]::UTF8.GetString($payload['UpscaleLab.Connect.runtimeconfig.json']) | ConvertFrom-Json
$frameworks = $runtime.runtimeOptions.includedFrameworks
$desktop = $frameworks | Where-Object name -EQ 'Microsoft.WindowsDesktop.App'
$core = $frameworks | Where-Object name -EQ 'Microsoft.NETCore.App'
if ($deps.runtimeTarget.name -ne '.NETCoreApp,Version=v8.0/win-x64' -or !$desktop -or !$core -or
    $desktop.version -ne $core.version -or $core.version -notmatch '^8\.0\.\d+$') {
    throw 'The EXE does not contain the expected self-contained Windows Desktop runtime.'
}

# Read the AssemblyMetadata attribute values without loading or executing the installer.
$assemblyContent = $encoding.GetString($payload['UpscaleLab.Connect.dll'])
foreach ($item in @{ DefaultApiBaseUrl = $ApiBaseUrl; LocalTest = 'false' }.GetEnumerator()) {
    $attribute = [Reflection.Metadata.BlobBuilder]::new()
    $attribute.WriteUInt16(1) # Custom attribute prolog
    $attribute.WriteSerializedString($item.Key)
    $attribute.WriteSerializedString($item.Value)
    $attribute.WriteUInt16(0) # No named arguments
    if (!$assemblyContent.Contains($encoding.GetString($attribute.ToArray()), [StringComparison]::Ordinal)) {
        throw "Unexpected assembly metadata for '$($item.Key)'."
    }
}
foreach ($type in @('InstallForm', 'ReinstallForm')) {
    if (!$assemblyContent.Contains("`0$type`0", [StringComparison]::Ordinal)) { throw "Missing installer type '$type'." }
}

$fileName = "UpscaleLab-Connect-$Version-win-x64.exe"
$hash = (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$release = [pscustomobject]@{
    fileName = $fileName
    version = $Version
    fileVersion = $versionInfo.FileVersion
    sizeBytes = $exe.Length
    sha256 = $hash
    authenticode = $signature.Status.ToString()
    runtimeVersion = $core.version
    bundleFiles = $fileCount
}
New-Item -ItemType Directory -Path $ArtifactDirectory | Out-Null
Copy-Item -LiteralPath $exe.FullName -Destination (Join-Path $ArtifactDirectory $fileName)
"$hash  $fileName" | Set-Content -LiteralPath (Join-Path $ArtifactDirectory "$fileName.sha256") -Encoding utf8NoBOM
$release | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'build-info.json') -Encoding utf8NoBOM
$release

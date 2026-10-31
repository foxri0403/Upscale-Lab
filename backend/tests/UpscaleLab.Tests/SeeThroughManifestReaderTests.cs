using System.IO.Compression;
using System.Text.Json;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Processing;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class SeeThroughManifestReaderTests
{
    [Fact]
    public async Task Manifest_MapsCharacterPartsAndKeepsLayerOrder()
    {
        using var temp = new TempDirectory();
        var layers = Path.Combine(temp.Path, "layers");
        Directory.CreateDirectory(layers);
        await File.WriteAllBytesAsync(Path.Combine(layers, "000.png"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(layers, "001.png"), [1]);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "manifest.json"),
            JsonSerializer.Serialize(new { layers = new[] {
                new { file = "layers/001.png", layerType = "Eye", layerOrder = 1,
                    depth = 0.5, positionX = 0, positionY = 0, rotation = 0, scale = 1, movementX = 3, movementY = 3 },
                new { file = "layers/000.png", layerType = "BackHair", layerOrder = 0,
                    depth = 0.0, positionX = 0, positionY = 0, rotation = 0, scale = 1, movementX = 4, movementY = 4 }
            } }));
        var result = await SeeThroughManifestReader.ReadAsync(temp.Path, CancellationToken.None);
        Assert.Equal(new[] { LayerType.BackHair, LayerType.Eye }, result.Select(x => x.LayerType).ToArray());
    }

    [Fact]
    public async Task Manifest_PrefersKnownSemanticLabelsForLayerEditor()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "layers"));
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "layers", "000.png"), [1]);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "manifest.json"),
            """{"layers":[{"file":"layers/000.png","layerType":"Face","semanticLayerType":"Eye","layerOrder":0,"depth":0,"positionX":0,"positionY":0,"rotation":0,"scale":1,"movementX":0,"movementY":0}]}""");
        var result = await SeeThroughManifestReader.ReadAsync(temp.Path, CancellationToken.None);
        Assert.Equal(LayerType.Eye, Assert.Single(result).LayerType);
    }

    [Fact]
    public async Task Manifest_RejectsTraversal()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "manifest.json"),
            """{"layers":[{"file":"layers/../../secret.png","layerType":"Eye","layerOrder":0,"depth":0,"positionX":0,"positionY":0,"rotation":0,"scale":1,"movementX":0,"movementY":0}]}""");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            SeeThroughManifestReader.ReadAsync(temp.Path, CancellationToken.None));
    }

    [Fact]
    public void Zip_RejectsTraversalAndAcceptsExpectedLayout()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "parts.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open())) writer.Write("{}");
            using (var writer = new StreamWriter(archive.CreateEntry("layers/000.png").Open())) writer.Write("abc");
        }
        var output = Path.Combine(temp.Path, "valid");
        Directory.CreateDirectory(output);
        RunPodPretrainedSeeThroughProcessor.ExtractValidatedArchive(archivePath, output);
        Assert.True(File.Exists(Path.Combine(output, "layers", "000.png")));

        var maliciousPath = Path.Combine(temp.Path, "malicious.zip");
        using (var archive = ZipFile.Open(maliciousPath, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open())) writer.Write("{}");
            using (var writer = new StreamWriter(archive.CreateEntry("layers/../../secret.png").Open())) writer.Write("abc");
        }
        var invalidOutput = Path.Combine(temp.Path, "invalid");
        Directory.CreateDirectory(invalidOutput);
        Assert.Throws<InvalidDataException>(() =>
            RunPodPretrainedSeeThroughProcessor.ExtractValidatedArchive(maliciousPath, invalidOutput));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "see-through-test", Guid.NewGuid().ToString("N"));
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

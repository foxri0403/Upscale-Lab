using System.Text.Json;
using UpscaleLab.Application.Processing;
using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Infrastructure.Processing;

/// <summary>Reads the trusted worker's manifest, validating layer types and paths.</summary>
public static class SeeThroughManifestReader
{
    public static async Task<IReadOnlyList<ProcessedLayer>> ReadAsync(
        string outputDirectory, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException("See-through 결과 manifest.json이 없습니다.");

        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<AdapterManifest>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken)
            ?? throw new InvalidDataException("See-through manifest 파일이 비어 있습니다.");
        if (manifest.Layers is null || manifest.Layers.Count is < 1 or > 64)
            throw new InvalidDataException("See-through 레이어 개수는 1~64개여야 합니다.");

        var outputRoot = Path.GetFullPath(outputDirectory) + Path.DirectorySeparatorChar;
        var layers = new List<ProcessedLayer>();
        var seenFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in manifest.Layers)
        {
            if (string.IsNullOrWhiteSpace(item.File) ||
                !item.File.StartsWith("layers/", StringComparison.Ordinal) ||
                !item.File.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                item.File.Contains('\\') || item.File.Contains("..", StringComparison.Ordinal) ||
                !seenFiles.Add(item.File))
                throw new InvalidDataException("See-through manifest의 레이어 파일 경로가 올바르지 않습니다.");

            var path = Path.GetFullPath(Path.Combine(outputDirectory, item.File));
            if (!path.StartsWith(outputRoot, StringComparison.Ordinal) || !File.Exists(path))
                throw new InvalidDataException("See-through 결과 PNG가 작업 폴더에 없습니다.");
            if (item.LayerOrder is < 0 or >= 64)
                throw new InvalidDataException("See-through 레이어 순서가 올바르지 않습니다.");
            if (!double.IsFinite(item.Depth) || !double.IsFinite(item.PositionX) ||
                !double.IsFinite(item.PositionY) || !double.IsFinite(item.Rotation) ||
                !double.IsFinite(item.Scale) || !double.IsFinite(item.MovementX) ||
                !double.IsFinite(item.MovementY))
                throw new InvalidDataException("See-through 레이어 좌표에 잘못된 숫자가 있습니다.");

            var layerType = Enum.TryParse<LayerType>(item.LayerType, ignoreCase: true, out var parsed) &&
                            Enum.IsDefined(parsed) ? parsed : LayerType.Other;
            layers.Add(new ProcessedLayer(
                path, layerType, item.LayerOrder, item.Depth, item.PositionX, item.PositionY,
                item.Rotation, item.Scale, item.MovementX, item.MovementY));
        }
        if (layers.Select(x => x.LayerOrder).Distinct().Count() != layers.Count)
            throw new InvalidDataException("See-through manifest의 레이어 순서가 중복됐습니다.");
        return layers.OrderBy(x => x.LayerOrder).ToArray();
    }

    private sealed record AdapterManifest(IReadOnlyList<AdapterLayer> Layers);
    private sealed record AdapterLayer(
        string File, string LayerType, int LayerOrder, double Depth, double PositionX,
        double PositionY, double Rotation, double Scale, double MovementX, double MovementY);
}

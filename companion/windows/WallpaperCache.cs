using System.Text.Json;

namespace UpscaleLab.Companion;

internal sealed class WallpaperCache(HttpClient downloads, Guid account, string server)
{
    private readonly string root = Path.Combine(SecureStore.Root, "images", account.ToString("N"),
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(server)))[..16]);
    public async Task<WallpaperManifest> Download(WallpaperManifest manifest, CancellationToken ct)
    {
        Validate(manifest);
        var directory = Path.Combine(root, manifest.AssetId.ToString("N"));
        var saved = Path.Combine(directory, "manifest.json");
        if (File.Exists(saved))
        {
            var cached = JsonSerializer.Deserialize<WallpaperManifest>(await File.ReadAllTextAsync(saved, ct), AgentApi.Json);
            if (cached != null && cached.AssetId == manifest.AssetId && cached.Mode == manifest.Mode && cached.Files.Length == manifest.Files.Length &&
                cached.Files.All(f => File.Exists(f.Source) && Path.GetDirectoryName(f.Source) == directory)) return cached;
        }
        Directory.CreateDirectory(directory);
        var files = new List<WallpaperFile>(); long total = 0;
        foreach (var file in manifest.Files.OrderBy(f => f.Order))
        {
            if (!Uri.TryCreate(file.Source, UriKind.Absolute, out var url) || url.Scheme != "https" || !string.IsNullOrEmpty(url.UserInfo))
                throw new InvalidDataException("이미지 다운로드 주소가 올바르지 않습니다.");
            if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("이미지 파일이 아닙니다.");
            const string extension = ".image";
            var path = Path.Combine(directory, $"layer-{file.Order:D3}{extension}");
            using var response = await downloads.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = File.Create(path + ".part"))
            {
                var buffer = new byte[65536]; int size; long current = 0;
                while ((size = await input.ReadAsync(buffer, ct)) > 0)
                {
                    current += size; total += size;
                    if (current > 64 * 1024 * 1024 || total > 200 * 1024 * 1024) throw new InvalidDataException("이미지 용량이 너무 큽니다.");
                    await output.WriteAsync(buffer.AsMemory(0, size), ct);
                }
            }
            File.Move(path + ".part", path, true);
            files.Add(file with { Source = path });
        }
        var local = manifest with { Files = files.ToArray() };
        await File.WriteAllTextAsync(saved + ".part", JsonSerializer.Serialize(local, AgentApi.Json), ct);
        File.Move(saved + ".part", saved, true);
        return local;
    }
    internal static void Validate(WallpaperManifest m)
    {
        if (m.Mode == "Static" && m.Files?.Length != 1 || m.Mode == "Live2D" && m.Files?.Any(f => f.Role == "Background") != true)
            throw new InvalidDataException("배경화면 레이어 구성이 올바르지 않습니다.");
        if (m.SchemaVersion != 1 || m.AssetId == Guid.Empty || m.Width is < 1 or > 32768 || m.Height is < 1 or > 32768 ||
            m.Mode is not ("Static" or "Live2D") || m.Files is null || m.Files.Length is < 1 or > 32 ||
            m.Files.Select(f => f.Order).Distinct().Count() != m.Files.Length || m.Files.Any(f => f.Order < 0 || f.Order > 1000 ||
                !double.IsFinite(f.Scale) || f.Scale is <= 0 or > 10 || !double.IsFinite(f.Rotation) ||
                !double.IsFinite(f.X) || !double.IsFinite(f.Y) || !double.IsFinite(f.MovementX) || !double.IsFinite(f.MovementY)))
            throw new InvalidDataException("배경화면 데이터가 올바르지 않습니다.");
    }
}

using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using ImageMagick;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Processing;
using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Infrastructure.Processing;

public sealed class LocalGpuImageProcessor(
    HttpClient httpClient, LocalGpuOptions options, IImageLayerProcessor legacyProcessor) : IProjectImageProcessor
{
    public async Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            if (request.Operation != ProcessingOperation.Layers || request.LayerEngine != LayerEngine.SeeThrough)
                throw new ConfigurationException("이 처리에는 LocalGpu:Enabled와 로컬 GPU 서버 설정이 필요합니다.");
            return new(await legacyProcessor.ProcessAsync(new(request.InputPath, request.OutputDirectory), cancellationToken), null, 0, 0);
        }
        options.Validate();
        var operation = request.Operation switch
        {
            ProcessingOperation.Layers => "layers",
            ProcessingOperation.Upscale => "upscale",
            ProcessingOperation.UpscaleAndLayers => "upscale_layers",
            _ => throw new ValidationException("지원하지 않는 처리 방식입니다.")
        };
        var engine = request.LayerEngine == LayerEngine.SeeThrough ? "see-through" : "partglee";
        Directory.CreateDirectory(request.OutputDirectory);
        var archivePath = Path.Combine(request.OutputDirectory, Guid.NewGuid().ToString("N") + ".zip");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(options.TimeoutMinutes));
        var token = deadline.Token;
        try
        {
            await using var source = File.OpenRead(request.InputPath);
            using var content = new MultipartFormDataContent();
            using var file = new StreamContent(source);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(file, "file", "input" + Path.GetExtension(request.InputPath));
            content.Add(new StringContent(operation), "operation");
            content.Add(new StringContent(engine), "engine");
            content.Add(new StringContent(request.UpscaleFactor.ToString(System.Globalization.CultureInfo.InvariantCulture)), "scale");
            using var message = new HttpRequestMessage(HttpMethod.Post, options.BaseUrl.TrimEnd('/') + "/v1/process") { Content = content };
            message.Headers.Add("X-Worker-Key", options.ApiKey);
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Local GPU worker returned HTTP {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentType?.MediaType != "application/zip" ||
                response.Content.Headers.ContentLength > options.MaxArchiveBytes)
                throw new InvalidDataException("Invalid GPU result archive response.");
            await using (var destination = File.Create(archivePath))
            await using (var remote = await response.Content.ReadAsStreamAsync(token))
                await CopyLimitedAsync(remote, destination, options.MaxArchiveBytes, token);
            return await ReadArchiveAsync(archivePath, request.OutputDirectory, operation, engine, token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"로컬 GPU 처리 시간이 {options.TimeoutMinutes}분을 초과했습니다.");
        }
        finally { File.Delete(archivePath); }
    }

    private async Task<ProjectProcessingResult> ReadArchiveAsync(
        string path, string output, string operation, string engine, CancellationToken token)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.Entries.Count is < 2 or > 66) throw new InvalidDataException("Invalid archive entry count.");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            ValidatePath(entry.FullName);
            if (!entries.TryAdd(entry.FullName, entry) || entry.Length > options.MaxExtractedBytes - total)
                throw new InvalidDataException("Duplicate or oversized archive entry.");
            total += entry.Length;
        }
        if (!entries.TryGetValue("manifest.json", out var manifestEntry) || manifestEntry.Length > 65536)
            throw new InvalidDataException("Missing or oversized manifest.");
        await using var manifestStream = manifestEntry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<Manifest>(manifestStream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, token)
            ?? throw new InvalidDataException("Invalid manifest.");
        if (manifest.SchemaVersion != 1 || manifest.Operation != operation || manifest.Engine != engine ||
            manifest.Layers is null || manifest.Layers.Count > 64)
            throw new InvalidDataException("Incompatible GPU manifest.");
        ValidateDimensions(manifest.Width, manifest.Height);
        var hasLayers = operation != "upscale";
        var hasImage = operation != "layers";
        if ((hasLayers && manifest.Layers.Count == 0) || (!hasLayers && manifest.Layers.Count != 0) ||
            hasImage != !string.IsNullOrWhiteSpace(manifest.ImageFile))
            throw new InvalidDataException("Missing or unexpected processing artifacts.");

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "manifest.json" };
        async Task<string> ExtractPng(string name, int width, int height)
        {
            ValidatePath(name);
            if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || !referenced.Add(name) || !entries.TryGetValue(name, out var entry))
                throw new InvalidDataException("Invalid PNG reference.");
            var target = Path.Combine(output, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using (var input = entry.Open())
            await using (var file = new FileStream(target, FileMode.CreateNew))
                await CopyLimitedAsync(input, file, entry.Length, token);
            var info = new MagickImageInfo(target);
            if (info.Format != MagickFormat.Png || info.Width != width || info.Height != height)
                throw new InvalidDataException("PNG dimensions do not match manifest.");
            return target;
        }

        string? imagePath = null;
        if (hasImage)
        {
            ValidateDimensions(manifest.ImageWidth, manifest.ImageHeight);
            imagePath = await ExtractPng(manifest.ImageFile!, manifest.ImageWidth, manifest.ImageHeight);
        }
        var layers = new List<ProcessedLayer>();
        var orders = new HashSet<int>();
        foreach (var item in manifest.Layers)
        {
            if (!orders.Add(item.LayerOrder) || item.LayerOrder is < 0 or >= 64 ||
                !Enum.TryParse<LayerType>(item.LayerType, true, out var type) || !Enum.IsDefined(type) ||
                !ValidNumber(item.Depth, -100, 100) || !ValidNumber(item.PositionX, -100000, 100000) ||
                !ValidNumber(item.PositionY, -100000, 100000) || !ValidNumber(item.Rotation, -360, 360) ||
                !ValidNumber(item.Scale, 0.01, 100) || !ValidNumber(item.MovementX, 0, 10000) || !ValidNumber(item.MovementY, 0, 10000))
                throw new InvalidDataException("Invalid layer metadata.");
            layers.Add(new(await ExtractPng(item.File, manifest.Width, manifest.Height), type, item.LayerOrder,
                item.Depth, item.PositionX, item.PositionY, item.Rotation, item.Scale, item.MovementX, item.MovementY));
        }
        if (referenced.Count != entries.Count) throw new InvalidDataException("Unexpected archive files.");
        return new(layers.OrderBy(x => x.LayerOrder).ToList(), imagePath, manifest.ImageWidth, manifest.ImageHeight);
    }

    private static bool ValidNumber(double number, double min, double max) => double.IsFinite(number) && number >= min && number <= max;
    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 64_000_000)
            throw new InvalidDataException("Invalid image dimensions.");
    }
    private static void ValidatePath(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '-' or '.')) ||
            name.Split('/').Any(p => p is "" or "." or "..") || name.Split('/').Any(p => p.EndsWith('.')))
            throw new InvalidDataException("Unsafe archive path.");
    }
    private static async Task CopyLimitedAsync(Stream input, Stream output, long limit, CancellationToken token)
    {
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            if (count > limit - total) throw new InvalidDataException("GPU result exceeds size limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            total += count;
        }
    }
    private sealed record Manifest(int SchemaVersion, string Operation, string Engine, int Width, int Height,
        string? ImageFile, int ImageWidth, int ImageHeight, List<ManifestLayer> Layers);
    private sealed record ManifestLayer(string File, string LayerType, int LayerOrder, double Depth,
        double PositionX, double PositionY, double Rotation, double Scale, double MovementX, double MovementY);
}

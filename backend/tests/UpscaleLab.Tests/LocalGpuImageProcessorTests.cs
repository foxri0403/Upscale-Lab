using System.IO.Compression;
using System.Net;
using System.Text.Json;
using ImageMagick;
using UpscaleLab.Application.Processing;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Processing;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class LocalGpuImageProcessorTests : IDisposable
{
    private readonly string workspace = Path.Combine(Path.GetTempPath(), "gpu-client-tests", Guid.NewGuid().ToString("N"));
    public LocalGpuImageProcessorTests() => Directory.CreateDirectory(workspace);
    public void Dispose() => Directory.Delete(workspace, true);

    [Fact]
    public async Task CombinedRequest_SendsAuthenticatedMultipartAndValidatesBothArtifacts()
    {
        var archive = CreateArchive();
        var handler = new Handler(async request =>
        {
            Assert.Equal("http://localhost:8090/v1/process", request.RequestUri!.ToString());
            Assert.Equal(new string('k', 32), request.Headers.GetValues("X-Worker-Key").Single());
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("upscale_layers", body);
            Assert.Contains("partglee", body);
            return ZipResponse(archive);
        });
        var result = await Run(handler);
        Assert.Single(result.Layers);
        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
        Assert.True(File.Exists(result.UpscaledFilePath));
        Assert.Equal(LayerType.Background, result.Layers[0].LayerType);
    }

    [Theory]
    [InlineData("../escaped.png")]
    [InlineData("C:/escaped.png")]
    [InlineData("layers\\escaped.png")]
    [InlineData("layers/../escaped.png")]
    public async Task UntrustedArchive_RejectsUnsafePaths(string name)
    {
        var handler = new Handler(_ => Task.FromResult(ZipResponse(CreateArchive(extraEntry: name))));
        await Assert.ThrowsAsync<InvalidDataException>(() => Run(handler));
    }

    [Fact]
    public async Task UntrustedArchive_RejectsMismatchedDimensions()
    {
        var handler = new Handler(_ => Task.FromResult(ZipResponse(CreateArchive(width: 3))));
        await Assert.ThrowsAsync<InvalidDataException>(() => Run(handler));
    }

    [Fact]
    public async Task UntrustedArchive_RejectsExpandedSizeLimit()
    {
        var handler = new Handler(_ => Task.FromResult(ZipResponse(CreateArchive())));
        await Assert.ThrowsAsync<InvalidDataException>(() => Run(handler, maxExtractedBytes: 10));
    }

    [Fact]
    public async Task WorkerFailure_DoesNotExposeResponseBody()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            { Content = new StringContent("sensitive-worker-trace") }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(handler));
        Assert.DoesNotContain("sensitive-worker-trace", error.Message);
    }

    [Fact]
    public async Task Cancellation_IsPropagatedAndArchiveRemoved()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var handler = new Handler(_ => throw new OperationCanceledException(cancel.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(handler, token: cancel.Token));
        Assert.Empty(Directory.GetFiles(workspace, "*.zip"));
    }

    private async Task<ProjectProcessingResult> Run(Handler handler, long maxExtractedBytes = 1024 * 1024, CancellationToken token = default)
    {
        var input = Path.Combine(workspace, "input.png");
        await File.WriteAllBytesAsync(input, Png());
        using var client = new HttpClient(handler);
        var processor = new LocalGpuImageProcessor(client, new LocalGpuOptions
        {
            Enabled = true, BaseUrl = "http://localhost:8090", ApiKey = new string('k', 32), MaxExtractedBytes = maxExtractedBytes
        }, new UnusedLegacy());
        return await processor.ProcessAsync(new(input, workspace, ProcessingOperation.UpscaleAndLayers, LayerEngine.PartGlee, 2), token);
    }

    private static HttpResponseMessage ZipResponse(byte[] archive)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
        response.Content.Headers.ContentType = new("application/zip");
        return response;
    }
    private static byte[] CreateArchive(string? extraEntry = null, int width = 2)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            var manifest = new
            {
                schemaVersion = 1, operation = "upscale_layers", engine = "partglee", width, height = 2,
                imageFile = "upscaled.png", imageWidth = 2, imageHeight = 2,
                layers = new[] { new { file = "layers/000.png", layerType = "Background", layerOrder = 0,
                    depth = 0d, positionX = 0d, positionY = 0d, rotation = 0d, scale = 1d, movementX = 0d, movementY = 0d } }
            };
            using (var file = zip.CreateEntry("manifest.json").Open()) JsonSerializer.Serialize(file, manifest);
            foreach (var name in new[] { "upscaled.png", "layers/000.png" })
            { using var file = zip.CreateEntry(name).Open(); file.Write(Png()); }
            if (extraEntry != null) { using var file = zip.CreateEntry(extraEntry).Open(); file.Write(Png()); }
        }
        return memory.ToArray();
    }
    private static byte[] Png()
    {
        using var image = new MagickImage(MagickColors.Red, 2, 2);
        return image.ToByteArray(MagickFormat.Png);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
    private sealed class UnusedLegacy : IImageLayerProcessor
    {
        public Task<IReadOnlyList<ProcessedLayer>> ProcessAsync(LayerProcessingRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Upscaling;
using UpscaleLab.Infrastructure.Upscaling;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class RunPodResolutionTests
{
    [Theory]
    [InlineData(1920, 1080, 2560, 2560, 1440)]
    [InlineData(1920, 1080, 3840, 3840, 2160)]
    [InlineData(1080, 1920, 2560, 1440, 2560)]
    [InlineData(1536, 1024, 2560, 2560, 1707)]
    [InlineData(960, 960, 3840, 3840, 3840)]
    public void DimensionsPreserveAspectRatio(int width, int height, int edge, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), UpscaleResolution.Dimensions(width, height, edge));
    }

    [Fact]
    public async Task FhdTo4KUsesBoundedRemoteTilesAndCombinesEveryEdge()
    {
        using var files = new Files();
        using (var image = new MagickImage(MagickColors.CornflowerBlue, 1920, 1080))
            image.Write(files.Input, MagickFormat.Png);
        using var handler = new Cloud();
        using var client = new HttpClient(handler);
        var service = new RunPodUpscaleService(client, Options());
        var progress = new List<int>();
        var result = await service.UpscaleToResolutionAsync(files.Input, 3840, files.Output,
            CancellationToken.None, (value, _) => { progress.Add(value); return Task.CompletedTask; });
        Assert.Equal(3840, result.Width);
        Assert.Equal(2160, result.Height);
        Assert.True(handler.Submissions > 1);
        Assert.Equal(handler.Submissions, progress.Count);
        Assert.Equal(75, progress[^1]);
        using var output = new MagickImage(files.Output);
        Assert.Equal(3840u, output.Width);
        Assert.Equal(2160u, output.Height);
        using var pixels = output.GetPixels();
        foreach (var (x, y) in new[] { (0, 0), (3839, 2159), (1000, 900), (895, 895) })
        {
            var color = pixels.GetPixel(x, y).ToColor()!;
            Assert.Equal(100, color.R);
            Assert.Equal(149, color.G);
            Assert.Equal(237, color.B);
        }
        Assert.Empty(Directory.GetDirectories(files.Root, ".tiles-*"));
    }

    [Fact]
    public async Task PortraitResultUsesTheExactSelectedLongEdge()
    {
        using var files = new Files();
        using (var image = new MagickImage(MagickColors.Red, 256, 384))
            image.Write(files.Input, MagickFormat.Png);
        using var handler = new Cloud();
        using var client = new HttpClient(handler);
        var result = await new RunPodUpscaleService(client, Options()).UpscaleToResolutionAsync(
            files.Input, 1920, files.Output, CancellationToken.None);
        Assert.Equal(1280, result.Width);
        Assert.Equal(1920, result.Height);
    }

    [Fact]
    public async Task FailedCloudTileDoesNotProduceALocalFallbackOrPartialFinalImage()
    {
        using var files = new Files();
        using (var image = new MagickImage(MagickColors.Red, 600, 400))
            image.Write(files.Input, MagickFormat.Png);
        using var handler = new Cloud { FailAtSubmission = 2 };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RunPodUpscaleService(client, Options())
            .UpscaleToResolutionAsync(files.Input, 1920, files.Output, CancellationToken.None));
        Assert.Equal(2, handler.Submissions);
        Assert.False(File.Exists(files.Output));
        Assert.Empty(Directory.GetDirectories(files.Root, ".tiles-*"));
    }

    [Fact]
    public async Task UnsupportedTargetsAndDownscalingNeverSubmitGpuJobs()
    {
        using var files = new Files();
        using (var image = new MagickImage(MagickColors.Red, 1920, 1080))
            image.Write(files.Input, MagickFormat.Png);
        using var handler = new Cloud();
        using var client = new HttpClient(handler);
        var service = new RunPodUpscaleService(client, Options());
        await Assert.ThrowsAsync<ValidationException>(() => service.UpscaleToResolutionAsync(files.Input, 4096, files.Output, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpscaleToResolutionAsync(files.Input, 1920, files.Output, CancellationToken.None));
        Assert.Equal(0, handler.Submissions);
    }

    private static RunPodOptions Options() => new() { ApiKey = "test-key", EndpointId = "test-endpoint", TimeoutSeconds = 60 };

    private sealed class Cloud : HttpMessageHandler
    {
        public int Submissions { get; private set; }
        public int FailAtSubmission { get; init; }
        private readonly Dictionary<string, object> outputs = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            if (request.Method == HttpMethod.Post)
            {
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var input = document.RootElement.GetProperty("input");
                var scale = input.GetProperty("scale").GetInt32();
                Assert.Contains(scale, new[] { 2, 4 });
                var bytes = Convert.FromBase64String(input.GetProperty("image_base64").GetString()!);
                Assert.True(bytes.Length <= 2 * 1024 * 1024);
                using var image = new MagickImage(bytes);
                Assert.True((long)image.Width * image.Height <= 1_000_000);
                Assert.True((long)image.Width * image.Height * scale * scale <= 4_000_000);
                image.Resize(new MagickGeometry(image.Width * (uint)scale, image.Height * (uint)scale) { IgnoreAspectRatio = true });
                var png = image.ToByteArray(MagickFormat.Png);
                Assert.True(png.Length <= 5 * 1024 * 1024);
                var id = (++Submissions).ToString();
                outputs[id] = new { status = "completed", width = (int)image.Width, height = (int)image.Height,
                    image_base64 = Convert.ToBase64String(png) };
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id, status = "IN_QUEUE" }) };
            }
            var job = request.RequestUri!.Segments[^1];
            if (int.Parse(job) == FailAtSubmission)
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { status = "FAILED", error = "Synthetic failure" }) };
            var output = outputs[job]; outputs.Remove(job);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { status = "COMPLETED", output }) };
        }
    }

    private sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "upscale-resolution-tests", Guid.NewGuid().ToString("N"));
        public string Input => Path.Combine(Root, "input.png");
        public string Output => Path.Combine(Root, "result.png");
        public Files() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "upscale-resolution-tests")) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(Root).StartsWith(parent, StringComparison.OrdinalIgnoreCase)) Directory.Delete(Root, true);
        }
    }
}

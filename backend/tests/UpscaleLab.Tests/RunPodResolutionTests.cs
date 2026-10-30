
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using UpscaleLab.Application.Upscaling;
using UpscaleLab.Infrastructure.Upscaling;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class RunPodResolutionTests
{
    [Theory]
    [InlineData(1920, "FHD")]
    [InlineData(2560, "QHD")]
    [InlineData(3840, "4K")]
    public async Task SendsCorrectResolutionToRunPod(
        int edge, string expected)
    {
        using var files = new Files();
        using (var image = new MagickImage(
            MagickColors.Red, 100, 100))
            image.Write(files.Input, MagickFormat.Png);

        using var handler = new Cloud();
        using var client = new HttpClient(handler);

        var service = new RunPodUpscaleService(
            client, Options());

        var result = await service.UpscaleAsync(
            files.Input, 4, files.Output,
            CancellationToken.None, edge);

        Assert.Equal(expected, handler.LastResolution);
        Assert.Equal(1, handler.Submissions);
        Assert.Equal(edge, result.Width);
        Assert.Equal(edge, result.Height);
        Assert.True(File.Exists(files.Output));
    }

    [Fact]
    public async Task FailedJobDoesNotCreateOutput()
    {
        using var files = new Files();
        using (var image = new MagickImage(
            MagickColors.Red, 100, 100))
            image.Write(files.Input, MagickFormat.Png);

        using var handler = new Cloud { Fail = true };
        using var client = new HttpClient(handler);

        var service = new RunPodUpscaleService(
            client, Options());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpscaleAsync(
                files.Input, 4, files.Output,
                CancellationToken.None, 1920));

        Assert.False(File.Exists(files.Output));
    }

    [Theory]
    [InlineData(1920, 1080, 2560, 2560, 1440)]
    [InlineData(1080, 1920, 2560, 1440, 2560)]
    [InlineData(960, 960, 3840, 3840, 3840)]
    public void DimensionsPreserveAspectRatio(
        int width, int height, int edge,
        int expectedWidth, int expectedHeight)
    {
        Assert.Equal(
            (expectedWidth, expectedHeight),
            UpscaleResolution.Dimensions(width, height, edge));
    }

    private static RunPodOptions Options() => new()
    {
        ApiKey = "test-key",
        EndpointId = "test-endpoint",
        TimeoutSeconds = 60
    };

    private sealed class Cloud : HttpMessageHandler
    {
        public int Submissions { get; private set; }
        public string? LastResolution { get; private set; }
        public bool Fail { get; init; }

        private readonly Dictionary<string, object> outputs = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                using var document = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(
                        cancellationToken));

                var input = document.RootElement.GetProperty("input");

                LastResolution = input
                    .GetProperty("resolution").GetString();

                var imageBytes = Convert.FromBase64String(
                    input.GetProperty("image_base64").GetString()!);

                using var image = new MagickImage(imageBytes);

                var edge = LastResolution switch
                {
                    "FHD" => 1920,
                    "QHD" => 2560,
                    "4K" => 3840,
                    _ => throw new InvalidOperationException()
                };

                var longest = Math.Max(image.Width, image.Height);

                var width = (uint)Math.Round(
                    image.Width * (double)edge / longest);

                var height = (uint)Math.Round(
                    image.Height * (double)edge / longest);

                image.Resize(new MagickGeometry(width, height)
                {
                    IgnoreAspectRatio = true
                });

                var png = image.ToByteArray(MagickFormat.Png);
                var id = (++Submissions).ToString();

                outputs[id] = new
                {
                    status = "completed",
                    width = (int)image.Width,
                    height = (int)image.Height,
                    image_base64 = Convert.ToBase64String(png)
                };

                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        id,
                        status = "IN_QUEUE"
                    })
                };
            }

            var jobId = request.RequestUri!.Segments[^1];

            if (Fail)
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        status = "FAILED",
                        error = "Synthetic failure"
                    })
                };

            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    status = "COMPLETED",
                    output = outputs[jobId]
                })
            };
        }
    }

    private sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            "runpod-v5-tests",
            Guid.NewGuid().ToString("N"));

        public string Input => Path.Combine(Root, "input.png");
        public string Output => Path.Combine(Root, "output.png");

        public Files()
        {
            Directory.CreateDirectory(Root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
    }
}

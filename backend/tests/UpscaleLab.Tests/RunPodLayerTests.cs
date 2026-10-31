using System.Net;
using System.Net.Http.Json;
using Amazon;
using Amazon.Runtime;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using ImageMagick;
using UpscaleLab.Application.Processing;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Processing;
using UpscaleLab.Infrastructure.Storage;
using UpscaleLab.Infrastructure.Upscaling;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class RunPodLayerTests
{
    [Fact]
    public async Task SeparateLayerCredentialIsUsedWithoutChangingUpscaleCredential()
    {
        using var files = new Workspace(); using var s3 = new S3Stub();
        using var cloud = new Cloud(s3); using var http = new HttpClient(cloud);
        var options = new RunPodOptions { ApiKey = "upscale-only", LayersApiKey = "layers-only",
            LayersEndpointId = "layers", ApiBaseUrl = "https://provider.test/v2/" };
        var processor = new RunPodImageLayerProcessor(http, options, s3, new S3StorageOptions { S3BucketName = "test" });
        await processor.ProcessAsync(new(files.Input, files.Output, Guid.NewGuid()), CancellationToken.None);
        Assert.NotEmpty(cloud.AuthorizationTokens);
        Assert.All(cloud.AuthorizationTokens, token => Assert.Equal("layers-only", token));
        Assert.Equal("upscale-only", options.ApiKey);
    }

    [Fact]
    public async Task CloudLayersPreserveCanvasAndSoftAlphaAndReuseDurableCompletion()
    {
        using var files = new Workspace();
        using var s3 = new S3Stub(); var store = s3;
        using var cloud = new Cloud(store); using var http = new HttpClient(cloud);
        var processor = Processor(http, s3); var id = Guid.NewGuid();
        var layers = await processor.ProcessAsync(new(files.Input, files.Output, id), CancellationToken.None);
        Assert.Equal(new[] { LayerType.Background, LayerType.Body }, layers.Select(layer => layer.LayerType));
        using var foreground = new MagickImage(layers[1].FilePath);
        Assert.Equal(40u, foreground.Width); Assert.Equal(60u, foreground.Height); Assert.True(foreground.HasAlpha);
        using var pixels = foreground.GetPixels();
        Assert.InRange(pixels.GetPixel(20, 30).ToColor()!.A, 126, 129);
        Assert.Equal(0, layers[0].MovementX); Assert.True(layers[1].MovementX > 0);
        await processor.ProcessAsync(new(files.Input, files.Output, id), CancellationToken.None);
        Assert.Equal(1, cloud.Submissions);
        Assert.DoesNotContain("secret-api-key", Encoding.UTF8.GetString(store.Files[$"cloud-jobs/layers/{id:N}/remote.json"]));
    }

    [Fact]
    public async Task OpaqueForegroundIsRejectedAndChangedInputNeverResubmitsSameJob()
    {
        using var files = new Workspace();
        using var s3 = new S3Stub();
        using var cloud = new Cloud(s3) { Opaque = true }; using var http = new HttpClient(cloud);
        var processor = Processor(http, s3); var id = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(new(files.Input, files.Output, id), CancellationToken.None));
        using (var changed = new MagickImage(MagickColors.Blue, 40, 60)) changed.Write(files.Input, MagickFormat.Png);
        await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(new(files.Input, files.Output, id), CancellationToken.None));
        Assert.Equal(1, cloud.Submissions);
    }

    [Fact]
    public async Task ProviderFailureDoesNotExposeProviderBodyOrCreateLocalLayers()
    {
        using var files = new Workspace(); using var s3 = new S3Stub();
        using var cloud = new Cloud(s3) { Fail = true }; using var http = new HttpClient(cloud);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Processor(http, s3)
            .ProcessAsync(new(files.Input, files.Output, Guid.NewGuid()), CancellationToken.None));
        Assert.DoesNotContain("sensitive-provider-body", error.Message);
        Assert.False(File.Exists(Path.Combine(files.Output, "foreground.png")));
    }

    [Fact]
    public async Task InterruptedApiResumesPersistedRemoteJobWithoutAnotherGpuSubmission()
    {
        using var files = new Workspace(); using var s3 = new S3Stub();
        using var shutdown = new CancellationTokenSource();
        using var cloud = new Cloud(s3) { PublishCompletion = false, CancelOnStatus = shutdown };
        using var http = new HttpClient(cloud); var id = Guid.NewGuid();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor(http, s3)
            .ProcessAsync(new(files.Input, files.Output, id), shutdown.Token));
        Assert.Contains($"cloud-jobs/layers/{id:N}/remote.json", s3.Files.Keys);
        cloud.PublishCompletion = true; cloud.CancelOnStatus = null;
        var layers = await Processor(http, s3).ProcessAsync(new(files.Input, files.Output, id), CancellationToken.None);
        Assert.Equal(2, layers.Count); Assert.Equal(1, cloud.Submissions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManualMaskIsUploadedAndBoundToCompletionAndRecovery(bool workerSupportsMask)
    {
        using var files = new Workspace(); using var s3 = new S3Stub();
        using var cloud = new Cloud(s3) { EchoMaskHash = workerSupportsMask }; using var http = new HttpClient(cloud);
        var maskPath = Path.Combine(files.Root, "mask.png");
        using (var mask = new MagickImage(MagickColors.Black, 40, 60)) mask.Write(maskPath, MagickFormat.Png);
        var id = Guid.NewGuid(); var processor = Processor(http, s3);
        var request = new LayerProcessingRequest(files.Input, files.Output, id, SubjectMaskPath: maskPath);
        if (workerSupportsMask)
        {
            Assert.Equal(2, (await processor.ProcessAsync(request, CancellationToken.None)).Count);
            await processor.ProcessAsync(request, CancellationToken.None);
            Assert.Equal(1, cloud.Submissions);
            using (var changed = new MagickImage(MagickColors.White, 40, 60)) changed.Write(maskPath, MagickFormat.Png);
            await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(request, CancellationToken.None));
        }
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(request, CancellationToken.None));
        Assert.Contains($"cloud-jobs/layers/{id:N}/mask.png", s3.Files.Keys);
        Assert.Equal(1, cloud.Submissions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SelectionRequiresWorkerAcknowledgementAndCannotSilentlyUseLegacyMask(bool supported)
    {
        using var files = new Workspace(); using var s3 = new S3Stub();
        using var cloud = new Cloud(s3) { EchoSelection = supported }; using var http = new HttpClient(cloud);
        var path = Path.Combine(files.Root, "selection.png");
        using (var mask = new MagickImage(MagickColors.Red, 40, 60)) mask.Write(path, MagickFormat.Png);
        var request = new LayerProcessingRequest(files.Input, files.Output, Guid.NewGuid(), SubjectMaskPath: path, RefineSelection: true);
        if (supported)
        {
            Assert.Equal(2, (await Processor(http, s3).ProcessAsync(request, CancellationToken.None)).Count);
            Assert.Equal(2, (await Processor(http, s3).ProcessAsync(request, CancellationToken.None)).Count);
        }
        else await Assert.ThrowsAsync<InvalidDataException>(() => Processor(http, s3).ProcessAsync(request, CancellationToken.None));
        Assert.Equal(1, cloud.Submissions);
    }

    private static RunPodImageLayerProcessor Processor(HttpClient http, IAmazonS3 s3) => new(http,
        new RunPodOptions { ApiKey = "secret-api-key", LayersEndpointId = "layers", ApiBaseUrl = "https://provider.test/v2/" },
        s3, new S3StorageOptions { S3BucketName = "test" });

    private sealed class Cloud(S3Stub store) : HttpMessageHandler
    {
        public readonly List<string?> AuthorizationTokens = [];
        public int Submissions; public bool Opaque; public bool Fail;
        public bool PublishCompletion = true;
        public bool EchoMaskHash = true;
        public bool EchoSelection = true;
        public CancellationTokenSource? CancelOnStatus;
        private string? pendingKey;
        private byte[]? pending;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            AuthorizationTokens.Add(request.Headers.Authorization?.Parameter);
            if (Fail) return new(HttpStatusCode.BadGateway) { Content = new StringContent("sensitive-provider-body") };
            if (request.Method == HttpMethod.Post)
            {
                Submissions++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var input = body.RootElement.GetProperty("input");
                Assert.False(input.TryGetProperty("apiKey", out _));
                var prefix = $"cloud-jobs/layers/{input.GetProperty("job_id").GetString()}/";
                using var background = new MagickImage(MagickColors.Blue, 40, 60);
                using var foreground = new MagickImage(Opaque ? MagickColors.Red : MagickColors.Transparent, 40, 60);
                if (!Opaque) { using var subject = new MagickImage(new MagickColor(255, 0, 0, 128), 20, 30);
                    foreground.Composite(subject, 10, 15, CompositeOperator.Over); }
                store.Files[prefix + "background.png"] = background.ToByteArray(MagickFormat.Png);
                store.Files[prefix + "foreground.png"] = foreground.ToByteArray(MagickFormat.Png);
                pendingKey = prefix + "complete.json";
                pending = JsonSerializer.SerializeToUtf8Bytes(new { status = "completed",
                    jobId = input.GetProperty("job_id").GetString(), inputSha256 = input.GetProperty("input_sha256").GetString(), width = 40, height = 60,
                    maskSha256 = EchoMaskHash && input.TryGetProperty("mask_sha256", out var hash) ? hash.GetString() : null,
                    maskMode = EchoSelection && input.TryGetProperty("mask_mode", out var mode) ? mode.GetString() : null });
                if (PublishCompletion) store.Files[pendingKey] = pending;
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = "remote-id" }) };
            }
            CancelOnStatus?.Cancel(); ct.ThrowIfCancellationRequested();
            if (PublishCompletion && pendingKey != null) store.Files[pendingKey] = pending!;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { status = "COMPLETED" }) };
        }
    }

    private sealed class S3Stub() : AmazonS3Client(new AnonymousAWSCredentials(), RegionEndpoint.USEast1), IAmazonS3
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        string IAmazonS3.GetPreSignedURL(GetPreSignedUrlRequest request) => "https://test.s3.amazonaws.com/file?X-Amz-Signature=test";
        public override Task<GetObjectResponse> GetObjectAsync(string bucket, string key, CancellationToken ct = default) => Read(key);
        public override Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken ct = default) => Read(request.Key);
        private Task<GetObjectResponse> Read(string key) => Files.TryGetValue(key, out var bytes)
            ? Task.FromResult(new GetObjectResponse { ResponseStream = new MemoryStream(bytes), ContentLength = bytes.Length })
            : Task.FromException<GetObjectResponse>(new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound });
        public override async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken ct = default)
        {
            using var bytes = new MemoryStream();
            if (request.InputStream != null) await request.InputStream.CopyToAsync(bytes, ct);
            Files[request.Key] = request.ContentBody != null ? Encoding.UTF8.GetBytes(request.ContentBody) : bytes.ToArray();
            return new();
        }
    }

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "layers-test-" + Guid.NewGuid().ToString("N"));
        public string Input => Path.Combine(Root, "input.png");
        public string Output => Path.Combine(Root, "output");
        public Workspace() { Directory.CreateDirectory(Root); using var image = new MagickImage(MagickColors.Red, 40, 60); image.Write(Input, MagickFormat.Png); }
        public void Dispose() => Directory.Delete(Root, true);
    }
}

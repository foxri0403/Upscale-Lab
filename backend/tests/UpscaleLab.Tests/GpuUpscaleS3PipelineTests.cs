using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
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

public sealed class GpuUpscaleS3PipelineTests
{
    [Theory]
    [InlineData(true, 1800, 1920)]
    [InlineData(true, 3600, 2560)]
    [InlineData(true, 600, 3840)]
    [InlineData(false, 600, 1920)]
    public async Task RealStorageSigningAndProjectProcessorReachGpuAndCleanTemporaryObjects(bool local, int timeout, int edge)
    {
        var root = Path.Combine(Path.GetTempPath(), "gpu-s3-pipeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.png");
            using (var image = new MagickImage(MagickColors.Red, 100, 75)) image.Write(input, MagickFormat.Png);
            using var s3 = new S3();
            var storageOptions = new S3StorageOptions { S3BucketName = "test" };
            var storage = new S3StorageService(s3, storageOptions);
            var options = local ? LocalGpuOptions.Select("LocalGpu", new RunPodOptions(), new LocalGpuOptions {
                ApiKey = new string('k', 48), TimeoutSeconds = timeout }) : new RunPodOptions {
                ApiKey = "cloud-key", EndpointId = "cloud-upscale", UseS3Transfer = true, TimeoutSeconds = timeout };
            using var cloud = new Cloud(s3);
            using var http = new HttpClient(cloud);
            var layers = new RunPodImageLayerProcessor(http, options, s3, storageOptions);
            var character = new RunPodPretrainedSeeThroughProcessor(http, new SeeThroughOptions(), storage, s3, storageOptions);
            var processor = new RunPodProjectImageProcessor(new ExistingLayerProjectProcessor(layers), character,
                new RunPodUpscaleService(http, options), new RunPodS3UpscaleService(http, options), options, storage);
            var before = DateTime.UtcNow;
            var result = await processor.ProcessAsync(new(input, Path.Combine(root, "output"),
                ProcessingOperation.Upscale, 4, Guid.NewGuid(), edge), CancellationToken.None);
            Assert.Equal(1, cloud.Submissions);
            Assert.Equal(edge, result.Width);
            Assert.Equal(edge * 3 / 4, result.Height);
            Assert.True(File.Exists(result.ImagePath));
            var upload = Assert.Single(s3.Signed, item => item.Verb == HttpVerb.PUT);
            var seconds = local ? timeout + 600 : 1800;
            Assert.InRange((upload.Expires!.Value - before).TotalSeconds, seconds - 2, seconds + 5);
            Assert.Equal("image/png", upload.ContentType);
            Assert.Equal(local ? timeout * 1000 : null, cloud.ExecutionTimeout);
            Assert.Empty(s3.Objects);
            Assert.Equal(2, s3.Deleted.Count);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(7201)]
    public void GpuUploadUrlStillRejectsUnboundedOrExpiredLifetimes(int seconds)
    {
        using var s3 = new S3();
        var storage = new S3StorageService(s3, new S3StorageOptions { S3BucketName = "test" });
        Assert.Throws<ArgumentOutOfRangeException>(() => storage.CreateUploadUrl("temporary/result.png", "image/png", TimeSpan.FromSeconds(seconds)));
        Assert.Empty(s3.Signed);
    }

    private sealed class S3() : AmazonS3Client(new AnonymousAWSCredentials(), RegionEndpoint.USEast1), IAmazonS3
    {
        public Dictionary<string, byte[]> Objects { get; } = [];
        public List<GetPreSignedUrlRequest> Signed { get; } = [];
        public List<string> Deleted { get; } = [];
        string IAmazonS3.GetPreSignedURL(GetPreSignedUrlRequest request)
        {
            Signed.Add(request);
            return "https://test.s3.amazonaws.com/" + request.Key + "?X-Amz-Signature=unit-test";
        }
        public override async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken ct = default)
        {
            using var bytes = new MemoryStream();
            await request.InputStream.CopyToAsync(bytes, ct);
            Objects[request.Key] = bytes.ToArray();
            return new();
        }
        public override Task<GetObjectResponse> GetObjectAsync(string bucket, string key, CancellationToken ct = default) =>
            Task.FromResult(new GetObjectResponse { ResponseStream = new MemoryStream(Objects[key]), ContentLength = Objects[key].Length });
        public override Task<DeleteObjectResponse> DeleteObjectAsync(string bucket, string key, CancellationToken ct = default)
        {
            Deleted.Add(key); Objects.Remove(key); return Task.FromResult(new DeleteObjectResponse());
        }
    }

    private sealed class Cloud(S3 s3) : HttpMessageHandler
    {
        public int Submissions;
        public int? ExecutionTimeout;
        private int width, height;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post)
            {
                Submissions++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var input = body.RootElement.GetProperty("input");
                Assert.Equal("upscale_s3", input.GetProperty("operation").GetString());
                if (body.RootElement.TryGetProperty("policy", out var policy)) ExecutionTimeout = policy.GetProperty("executionTimeout").GetInt32();
                var inputKey = Uri.UnescapeDataString(new Uri(input.GetProperty("input_url").GetString()!).AbsolutePath.TrimStart('/'));
                var outputKey = Uri.UnescapeDataString(new Uri(input.GetProperty("output_url").GetString()!).AbsolutePath.TrimStart('/'));
                using var image = new MagickImage(s3.Objects[inputKey]);
                width = input.GetProperty("target_long_edge").GetInt32();
                height = width * 3 / 4;
                image.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
                s3.Objects[outputKey] = image.ToByteArray(MagickFormat.Png);
                return Response(new { id = "gpu-job", status = "IN_QUEUE" });
            }
            return Response(new { status = "COMPLETED", output = new { status = "completed", transfer = "s3", width, height } });
        }
        private static HttpResponseMessage Response(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}

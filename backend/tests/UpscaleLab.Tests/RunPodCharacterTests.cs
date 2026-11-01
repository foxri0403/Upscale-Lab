using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using UpscaleLab.Application.Processing;
using UpscaleLab.Application.Storage;
using UpscaleLab.Infrastructure.Processing;
using UpscaleLab.Infrastructure.Storage;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class RunPodCharacterTests
{
    [Fact]
    public async Task UsesConfiguredExecutionLimitAndReportsRemoteStagesThenReusesDurableResult()
    {
        using var files = new Workspace(); using var store = new Store();
        using var cloud = new Cloud(store); using var http = new HttpClient(cloud);
        var progress = new List<int>();
        var request = new LayerProcessingRequest(files.Input, files.Output, Guid.NewGuid(),
            (value, _) => { progress.Add(value); return Task.CompletedTask; });
        var layers = await Processor(http, store).ProcessAsync(request, CancellationToken.None);
        Assert.Single(layers);
        Assert.Equal(new[] { 20, 45, 70, 75 }, progress);
        Assert.Equal(2400000, cloud.ExecutionTimeout);
        Assert.Equal(2400000, cloud.Ttl);
        Assert.Equal("character-secret", cloud.Auth);
        Assert.Equal(1, cloud.Submissions);
        var statuses = cloud.Statuses;
        Directory.Delete(files.Output, true);
        Assert.Single(await Processor(http, store).ProcessAsync(request, CancellationToken.None));
        Assert.Equal(1, cloud.Submissions);
        Assert.Equal(statuses, cloud.Statuses);
    }

    [Fact]
    public async Task ShutdownPreservesRemoteIdAndResumesWithoutSubmittingAnotherGpuJob()
    {
        using var files = new Workspace(); using var store = new Store();
        using var cloud = new Cloud(store); using var http = new HttpClient(cloud);
        using var shutdown = new CancellationTokenSource();
        var id = Guid.NewGuid();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor(http, store).ProcessAsync(
            new(files.Input, files.Output, id, (_, _) => { shutdown.Cancel(); return Task.CompletedTask; }), shutdown.Token));
        Assert.Contains($"cloud-jobs/character-layers/{id:N}/remote.json", store.Files.Keys);
        Assert.Equal(0, cloud.Cancels);
        Assert.Single(await Processor(http, store).ProcessAsync(new(files.Input, files.Output, id), CancellationToken.None));
        Assert.Equal(1, cloud.Submissions);
    }

    [Fact]
    public async Task ChangedInputCannotReuseOrOverwriteRemoteWork()
    {
        using var files = new Workspace(); using var store = new Store();
        using var cloud = new Cloud(store); using var http = new HttpClient(cloud);
        var request = new LayerProcessingRequest(files.Input, files.Output, Guid.NewGuid());
        await Processor(http, store).ProcessAsync(request, CancellationToken.None);
        File.WriteAllText(files.Input, "different input");
        await Assert.ThrowsAsync<InvalidDataException>(() => Processor(http, store).ProcessAsync(request, CancellationToken.None));
        Assert.Equal(1, cloud.Submissions);
    }

    [Fact]
    public async Task ExpiredRecoveredJobIsCancelledInsteadOfRestartingItsDeadline()
    {
        using var files = new Workspace(); using var store = new Store();
        using var cloud = new Cloud(store); using var http = new HttpClient(cloud);
        var id = Guid.NewGuid(); var key = $"cloud-jobs/character-layers/{id:N}/remote.json";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(files.Input)));
        store.Files[key] = JsonSerializer.SerializeToUtf8Bytes(new { id = "saved-job", inputSha256 = hash,
            endpointId = "characters", submittedAt = DateTime.UtcNow.AddHours(-2), completed = false });
        await Assert.ThrowsAsync<TimeoutException>(() => Processor(http, store).ProcessAsync(
            new(files.Input, files.Output, id), CancellationToken.None));
        Assert.Equal(1, cloud.Cancels); Assert.Equal(0, cloud.Submissions);
    }

    [Fact]
    public async Task ProviderHttpErrorsDoNotExposeResponseOrSignedUrl()
    {
        using var files = new Workspace(); using var store = new Store();
        using var cloud = new Cloud(store) { Fail = true }; using var http = new HttpClient(cloud);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Processor(http, store).ProcessAsync(
            new(files.Input, files.Output, Guid.NewGuid()), CancellationToken.None));
        Assert.Contains("HTTP 502", error.Message);
        Assert.DoesNotContain("sensitive", error.Message); Assert.DoesNotContain("https", error.Message);
    }

    private static RunPodPretrainedSeeThroughProcessor Processor(HttpClient http, Store store) => new(http,
        new SeeThroughOptions { Enabled = true, Provider = "RunPod", RunPodApiKey = "character-secret",
            RunPodEndpointId = "characters", RunPodApiBaseUrl = "https://provider.test/v2", RunPodTimeoutSeconds = 2400 },
        store, store, new S3StorageOptions { S3BucketName = "test" });

    private sealed class Cloud(Store store) : HttpMessageHandler
    {
        public int Submissions, Statuses, Cancels, ExecutionTimeout, Ttl;
        public string? Auth; public bool Fail;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Auth = request.Headers.Authorization?.Parameter;
            if (Fail) return new(HttpStatusCode.BadGateway) { Content = new StringContent("sensitive response") };
            if (request.RequestUri!.AbsolutePath.Contains("/cancel/")) { Cancels++; return Response(new { status = "CANCELLED" }); }
            if (request.Method == HttpMethod.Post)
            {
                Submissions++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                ExecutionTimeout = body.RootElement.GetProperty("policy").GetProperty("executionTimeout").GetInt32();
                Ttl = body.RootElement.GetProperty("policy").GetProperty("ttl").GetInt32();
                var output = new Uri(body.RootElement.GetProperty("input").GetProperty("output_url").GetString()!);
                using var bytes = new MemoryStream();
                using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
                {
                    using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
                        writer.Write("""{"layers":[{"file":"layers/000.png","layerType":"Face","layerOrder":0,"scale":1}]}""");
                    using (var writer = new StreamWriter(zip.CreateEntry("layers/000.png").Open())) writer.Write("layer pixels");
                }
                store.Files[output.AbsolutePath.TrimStart('/')] = bytes.ToArray();
                return Response(new { id = "remote-id" });
            }
            Statuses++;
            return Response(new { status = Statuses == 1 ? "IN_QUEUE" : Statuses == 2 ? "IN_PROGRESS" : "COMPLETED",
                output = new { status = "completed" } });
        }
        private static HttpResponseMessage Response(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class Store() : AmazonS3Client(new AnonymousAWSCredentials(), RegionEndpoint.USEast1), IAmazonS3, IStorageService
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        string IAmazonS3.GetPreSignedURL(GetPreSignedUrlRequest request) => "https://storage.test/" + request.Key;
        public override Task<GetObjectResponse> GetObjectAsync(string bucket, string key, CancellationToken ct = default) =>
            Files.TryGetValue(key, out var value) ? Task.FromResult(new GetObjectResponse {
                ResponseStream = new MemoryStream(value), ContentLength = value.Length }) :
            Task.FromException<GetObjectResponse>(new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound });
        public override Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken ct = default)
        { Files[request.Key] = Encoding.UTF8.GetBytes(request.ContentBody); return Task.FromResult(new PutObjectResponse()); }
        public async Task<StoredObject> UploadAsync(Stream content, string key, string type, CancellationToken ct)
        { using var bytes = new MemoryStream(); await content.CopyToAsync(bytes, ct); Files[key] = bytes.ToArray(); return new(key, key); }
        public async Task DownloadAsync(string key, Stream destination, CancellationToken ct) => await destination.WriteAsync(Files[key], ct);
        public Task DeleteAsync(string key, CancellationToken ct) { Files.Remove(key); return Task.CompletedTask; }
        public string CreateDownloadUrl(string key, TimeSpan lifetime) => "https://storage.test/" + key;
        public string CreateUploadUrl(string key, string type, TimeSpan lifetime) => throw new NotSupportedException();
    }

    private sealed class Workspace : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "characters-test-" + Guid.NewGuid().ToString("N"));
        public string Input => Path.Combine(root, "input.png"); public string Output => Path.Combine(root, "output");
        public Workspace() { Directory.CreateDirectory(root); File.WriteAllText(Input, "test input"); }
        public void Dispose() => Directory.Delete(root, true);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using UpscaleLab.Infrastructure.Processing;
using UpscaleLab.Infrastructure.Upscaling;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class LocalGpuOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("RunPod")]
    public void ExistingProviderRemainsTheDefault(string? provider)
    {
        var cloud = new RunPodOptions { ApiKey = "cloud-only", EndpointId = "existing" };
        Assert.Same(cloud, LocalGpuOptions.Select(provider, cloud, new()));
    }

    [Fact]
    public void LocalProviderUsesOnlyItsOwnCredentialsAndS3Protocol()
    {
        var cloud = new RunPodOptions { ApiKey = "cloud-secret", EndpointId = "cloud-upscale", LayersEndpointId = "cloud-layers" };
        var selected = LocalGpuOptions.Select("LocalGpu", cloud, new() { ApiKey = new('x', 48) });
        Assert.Equal("http://127.0.0.1:18188/v2/", selected.ApiBaseUrl);
        Assert.Equal(new string('x', 48), selected.ApiKey);
        Assert.Equal(selected.ApiKey, selected.LayersApiKey);
        Assert.Equal("local-upscale", selected.EndpointId);
        Assert.Equal("local-layers", selected.LayersEndpointId);
        Assert.True(selected.UseS3Transfer);
        Assert.True(selected.UseJobDeadlinePolicy);
        Assert.Equal(1800, selected.LayersTimeoutSeconds);
        Assert.Equal("cloud-secret", cloud.ApiKey);
    }

    [Fact]
    public void MissingLocalKeyDoesNotUseCloudKey()
    {
        var selected = LocalGpuOptions.Select("LocalGpu", new() { ApiKey = "cloud-secret" }, new());
        Assert.False(new RunPodUpscaleService(new HttpClient(), selected).IsConfigured);
    }

    [Fact]
    public void DisabledOperationsRemainUnavailable()
    {
        var selected = LocalGpuOptions.Select("LocalGpu", new(), new()
            { ApiKey = new('x', 48), UpscaleEnabled = false, LayersEnabled = false });
        Assert.Empty(selected.EndpointId);
        Assert.Empty(selected.LayersEndpointId);
    }

    [Theory]
    [InlineData("http://gpu.example.com/v2/")]
    [InlineData("https://user:password@gpu.example.com/v2/")]
    [InlineData("https://gpu.example.com/v2/?key=secret")]
    [InlineData("file:///tmp/worker")]
    public void RejectsUnsafeTransport(string url) => Assert.Throws<InvalidOperationException>(() =>
        LocalGpuOptions.Select("LocalGpu", new(), new() { ApiBaseUrl = url }));

    [Fact]
    public void UnknownProviderFailsInsteadOfSilentlyUsingCloud() => Assert.Throws<InvalidOperationException>(() =>
        LocalGpuOptions.Select("LocalGup", new(), new()));

    [Theory]
    [InlineData(0)]
    [InlineData(3601)]
    public void RejectsInvalidDeadline(int seconds) => Assert.Throws<InvalidOperationException>(() =>
        LocalGpuOptions.Select("LocalGpu", new(), new() { TimeoutSeconds = seconds }));

    [Fact]
    public async Task S3AdapterSubmitsAndPollsOnlyTheSelectedTunnel()
    {
        var options = LocalGpuOptions.Select("LocalGpu", new() { ApiKey = "cloud-only" }, new() { ApiKey = new('x', 48) });
        using var handler = new Worker();
        using var client = new HttpClient(handler);
        var result = await new RunPodS3UpscaleService(client, options).UpscaleAsync(
            "https://input.invalid", "https://output.invalid", 2, CancellationToken.None, 1920);
        Assert.Equal(new RunPodS3Result(1920, 1080), result);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, uri => Assert.StartsWith("http://127.0.0.1:18188/v2/local-upscale/", uri));
    }

    [Fact]
    public async Task UnreachableWorkerDoesNotSubmitToCloud()
    {
        var options = LocalGpuOptions.Select("LocalGpu", new() { ApiKey = "cloud-only" }, new() { ApiKey = new('x', 48) });
        using var handler = new Worker { Unavailable = true };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => new RunPodS3UpscaleService(client, options).UpscaleAsync(
            "https://input.invalid", "https://output.invalid", 2, CancellationToken.None, 1920));
        Assert.Single(handler.Requests);
    }

    private sealed class Worker : HttpMessageHandler
    {
        public bool Unavailable { get; init; }
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            Assert.Equal(new string('x', 48), request.Headers.Authorization!.Parameter);
            if (Unavailable) return new(HttpStatusCode.ServiceUnavailable);
            if (request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var input = body.RootElement.GetProperty("input");
                Assert.Equal("upscale_s3", input.GetProperty("operation").GetString());
                Assert.Equal(1920, input.GetProperty("target_long_edge").GetInt32());
                Assert.Equal(1800000, body.RootElement.GetProperty("policy").GetProperty("ttl").GetInt32());
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = "local-job", status = "IN_QUEUE" }) };
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new
                { id = "local-job", status = "COMPLETED", output = new { status = "completed", transfer = "s3", width = 1920, height = 1080 } }) };
        }
    }
}

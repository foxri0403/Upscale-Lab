using System.Net;
using System.Net.Http.Json;
using UpscaleLab.Companion;
using Xunit;

public class AgentApiTests
{
    private static Login Session(Guid id, string access = "account-session") => new(access, "refresh-session", DateTime.UtcNow.AddMinutes(5), new(id, "test@example.invalid", "test"));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(action(request));
    }
    [Fact]
    public async Task AgentRequestRequiresBothMatchingCredentialsAndKeepsDeviceTokenOffAccountRoutes()
    {
        var owner = Guid.NewGuid(); var seen = 0;
        using var http = new HttpClient(new Handler(request => {
            seen++; Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("account-session", request.Headers.Authorization?.Parameter);
            Assert.Equal(request.RequestUri!.AbsolutePath.StartsWith("/api/agent/"), request.Headers.Contains("X-Device-Token"));
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { }) };
        }));
        var api = new AgentApi("https://example.invalid", http) { Session = Session(owner) };
        await Assert.ThrowsAsync<ApiFailure>(() => api.Call<object>("/api/agent/state", agent: true));
        api.Device = new(api.Server, Guid.NewGuid(), Guid.NewGuid(), "device-secret");
        await Assert.ThrowsAsync<ApiFailure>(() => api.Call<object>("/api/agent/state", agent: true));
        Assert.Equal(0, seen);
        api.Device = api.Device with { UserId = owner };
        await api.Call<object>("/api/agent/state", agent: true);
        await api.Call<object>("/api/auth/me"); Assert.Equal(2, seen);
    }
    [Fact]
    public async Task ExpiredAccountSessionIsRenewedWithoutSendingDeviceKeyToRefreshEndpoint()
    {
        var owner = Guid.NewGuid(); var calls = 0; var saved = false;
        using var http = new HttpClient(new Handler(request => {
            calls++;
            if (calls == 1) return new(HttpStatusCode.Unauthorized);
            if (calls == 2) {
                Assert.Equal("/api/auth/refresh", request.RequestUri!.AbsolutePath);
                Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("X-Device-Token"));
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(Session(owner, "renewed"), options: AgentApi.Json) };
            }
            Assert.Equal("renewed", request.Headers.Authorization?.Parameter);
            Assert.True(request.Headers.Contains("X-Device-Token"));
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { inventoryRevision = 7, displays = Array.Empty<object>() }) };
        }));
        var api = new AgentApi("https://example.invalid", http) { Session = Session(owner), Device = new("https://example.invalid", owner, Guid.NewGuid(), "device-secret"), SessionChanged = () => saved = true };
        var state = await api.Call<AgentState>("/api/agent/state", agent: true);
        Assert.Equal(7, state.InventoryRevision); Assert.Equal(3, calls); Assert.True(saved);
    }
    [Theory]
    [InlineData("file:///tmp/test")] [InlineData("https://user:password@example.invalid")]
    [InlineData("https://example.invalid/path")] [InlineData("https://example.invalid?token=secret")]
    public void ServerMustBeAnOriginWithoutEmbeddedCredentials(string input) => Assert.Throws<ArgumentException>(() => AgentApi.NormalizeServer(input));

    [Fact]
    public void InvalidLiveLayersAreRejectedBeforeDownloading()
    {
        var manifest = new WallpaperManifest(1, Guid.NewGuid(), "Live2D", 1920, 1080,
            [new("https://example.invalid/a", "image/png", 0, "Background", Scale: double.NaN)]);
        Assert.Throws<InvalidDataException>(() => WallpaperCache.Validate(manifest));
        Assert.Throws<InvalidDataException>(() => WallpaperCache.Validate(manifest with { Files = [manifest.Files[0] with { Scale = 1, Role = "Foreground" }] }));
    }
}

using System.Net;
using System.Net.Http.Json;
using UpscaleLab.Companion;
using Xunit;

public sealed class InventoryPrivacyTests
{
    private sealed class LoginHandler(bool success) : HttpMessageHandler {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(success ? HttpStatusCode.OK : HttpStatusCode.Unauthorized) {
                Content = success ? JsonContent.Create(new Login("access", "refresh", DateTime.UtcNow.AddHours(1), new(Guid.NewGuid(), "test@example.invalid", "test")), options: AgentApi.Json)
                    : JsonContent.Create(new { message = "로그인 실패" })
            });
        }
    }

    [Fact]
    public async Task MissingConsentBlocksLoginAndRestoreWithoutInventoryOrNetworkAccess() {
        var handler = new LoginHandler(true); var reads = 0;
        using var controller = new CompanionController(new(handler), () => false, () => { reads++; return []; },
            () => throw new Exception("Stored credentials must not be opened before consent"), _ => { }, allowTestLogin: false);
        controller.RefreshInventory(); await controller.Restore();
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SignIn("test", "password", false));
        Assert.Equal(0, handler.Requests); Assert.Equal(0, reads); Assert.Empty(controller.Monitors);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task InventoryStartsOnlyAfterSuccessfulLoginAndStopsOnLogout(bool success) {
        var handler = new LoginHandler(success); var reads = 0;
        var monitor = new MonitorInfo("test", "Test monitor", 1920, 1080, 1, 0, 0, 0, true, true, 60);
        using var controller = new CompanionController(new(handler), () => true, () => {
            reads++;
            // End registration before starting a background sync loop in this focused test.
            if (reads == 1) return [];
            return [monitor];
        }, () => null, _ => { }, allowTestLogin: false);
        controller.RefreshInventory(); Assert.Equal(0, reads);
        await Assert.ThrowsAnyAsync<Exception>(() => controller.SignIn("test", "password", false));
        Assert.Equal(success, controller.LoggedIn); Assert.Equal(success ? 1 : 0, reads);
        controller.RefreshInventory();
        Assert.Equal(success ? 2 : 0, reads); Assert.Equal(success ? 1 : 0, controller.Monitors.Length);
        await controller.Logout(); controller.RefreshInventory();
        Assert.Equal(success ? 2 : 0, reads); Assert.Empty(controller.Monitors); Assert.False(controller.LoggedIn);
    }
}

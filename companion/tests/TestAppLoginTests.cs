using System.Net;
using System.Net.Http.Json;
using UpscaleLab.Companion;
using Xunit;

public sealed class TestAppLoginTests
{
    private sealed class RejectLogin : HttpMessageHandler {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = JsonContent.Create(new { message = "로그인 실패" }) });
        }
    }

    [Theory]
    [InlineData("anything", "anything")]
    [InlineData("", "")]
    [InlineData("임의 입력", "123")]
    public async Task ArbitraryLoginIsAnOfflinePreviewOnlyInTestBuilds(string identifier, string password) {
        var handler = new RejectLogin(); var reads = 0;
        using var controller = new CompanionController(new(handler), () => true,
            () => { reads++; return [new("test", "Test screen", 1920, 1080, 1, 0, 0, 0, true, true, 60)]; },
            () => null, _ => throw new Exception("Preview must not store API credentials"));
        Assert.Equal(CompanionConfiguration.LocalTest, controller.TestApp);
        controller.RefreshInventory(); Assert.Equal(0, reads);
        if (!controller.TestApp) {
            await Assert.ThrowsAsync<ApiFailure>(() => controller.SignIn(identifier, password, true));
            Assert.Equal(1, handler.Requests); Assert.False(controller.LoggedIn); Assert.Equal(0, reads); return;
        }
        await controller.Restore(); Assert.False(controller.LoggedIn);
        await controller.SignIn(identifier, password, true);
        Assert.True(controller.LoggedIn); Assert.True(controller.Enabled); Assert.Single(controller.Monitors);
        Assert.Equal(string.IsNullOrWhiteSpace(identifier) ? "테스트 사용자" : identifier.Trim(), controller.Account);
        await controller.Stop(); Assert.False(controller.Enabled);
        await controller.Enable(); await controller.Reconnect();
        Assert.True(controller.Enabled); Assert.Equal(0, handler.Requests);
        await controller.Logout(); controller.RefreshInventory();
        Assert.False(controller.LoggedIn); Assert.Empty(controller.Monitors); Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task TestLoginStillRequiresConsentAndDoesNotReadScreensBeforeIt() {
        using var controller = new CompanionController(new(new RejectLogin()), () => false,
            () => throw new Exception("No display collection before consent"), () => null, _ => { });
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SignIn("", "", false));
        Assert.False(controller.LoggedIn); Assert.Empty(controller.Monitors);
    }
}

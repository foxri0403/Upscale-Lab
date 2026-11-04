using System.Net;
using System.Net.Http.Json;
using UpscaleLab.Companion;
using Xunit;

public sealed class ConnectionExperienceTests
{
    private sealed class ProfileHandler(Guid owner, bool matching) : HttpMessageHandler {
        internal List<string> Calls = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            var path = request.RequestUri!.AbsolutePath; Calls.Add(path);
            object body;
            if (path == "/api/auth/login") body = new Login("access", "refresh", DateTime.UtcNow.AddHours(1), new(owner, "owner@example.invalid", "owner"));
            else {
                Assert.Equal(HttpMethod.Get, request.Method); Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.False(request.Headers.Contains("X-Device-Token"));
                body = path == "/api/account" ? new AccountProfile(matching ? owner : Guid.NewGuid(), "owner@example.invalid", "updated-name", null)
                    : new AccountWallet("Pro");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, options: AgentApi.Json) });
        }
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task AccountPopupReadsMatchingProfileAndPlanWithoutAnyEditing(bool matching) {
        var handler = new ProfileHandler(Guid.NewGuid(), matching);
        using var controller = new CompanionController(new(handler), () => true, () => [], () => null, _ => { });
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SignIn("owner", "password", false));
        await controller.RefreshProfile();
        Assert.Equal(matching ? "updated-name" : "owner", controller.Username);
        Assert.Equal(matching ? "Pro" : "확인 불가", controller.Plan);
        if (matching) Assert.NotNull(controller.Profile); else Assert.Null(controller.Profile);
        Assert.Equal(matching, handler.Calls.Contains("/api/tokens/me"));
    }
    [Fact]
    public async Task ShortcutActivationSignalsExistingInstanceAndTimesOutWithoutOne() {
        var name = "UpscaleLabActivationTest-" + Guid.NewGuid().ToString("N");
        using var existing = new InstanceActivation(name);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        existing.Listen(() => opened.TrySetResult());
        using var shortcut = new InstanceActivation(name);
        Assert.True(await shortcut.Activate(TimeSpan.FromSeconds(5)));
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var absent = new InstanceActivation(name + "-absent");
        Assert.False(await absent.Activate(TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public void SavedIdentifierIsEncryptedAndCanBeRemovedWithoutAnAccountSession() {
        var root = Path.Combine(Path.GetTempPath(), "UpscaleLabIdentityTests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "identifier.bin");
        try {
            var settings = new LoginPreferences(file);
            Assert.Equal("", settings.Read()); settings.Save(true, "  remembered-user  ");
            Assert.Equal("remembered-user", new LoginPreferences(file).Read());
            Assert.DoesNotContain("remembered-user", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)));
            settings.Save(false, "remembered-user"); Assert.False(File.Exists(file)); Assert.Equal("", settings.Read());
        } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void MonitorLogPlacesPrimaryLabelAfterAllMetadata() {
        var monitor = new MonitorInfo("one", "화면 A", 2560, 1440, 1, 0, 0, 0, true, true, 144);
        Assert.Equal("화면 A | 2560 × 1440 | 144 Hz - 주 화면", MainForm.FormatMonitor(monitor));
        Assert.DoesNotContain("주 화면", MainForm.FormatMonitor(monitor with { IsPrimary = false }));
    }
}

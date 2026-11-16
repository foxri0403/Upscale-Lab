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

    [Fact]
    public async Task MainWindowKeepsItsDefaultSizeAndFitsControlsWithoutScroll()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                MonitorInfo[] displays = [];
                var controller = new CompanionController(new(new ProfileHandler(Guid.NewGuid(), true)),
                    () => true, () => displays, () => null, _ => { });
                using var form = new MainForm(controller) { ShowInTaskbar = false, Opacity = 0 };
                form.Show();
                var initial = form.Size;
                var resize = new Size(200, 80);
                Assert.False(form.AutoScroll);
                Assert.Equal(form.MinimumSize, initial);
                Assert.Equal((int)Math.Round(664 * form.DeviceDpi / 96.0), form.ClientSize.Width);
                Assert.Equal((int)Math.Round(588 * form.DeviceDpi / 96.0), form.ClientSize.Height);
                Assert.All(Controls(form).Where(x => x.Visible), control =>
                    Assert.True(form.ClientRectangle.Contains(Bounds(control)), $"{control.Text}: {Bounds(control)}"));

                // Establish a verified mock account without an agent connection.
                Assert.Throws<InvalidOperationException>(() => controller.SignIn("owner", "password", false).GetAwaiter().GetResult());
                displays = Enumerable.Range(1, 6).Select(index => new MonitorInfo(index.ToString(), $"화면 {index}",
                    1920, 1080, 1, 0, 0, 0, index == 1, true, 60)).ToArray();
                controller.RefreshInventory();
                var screenList = Assert.Single(Controls(form).OfType<ComboBox>(), x => x.AccessibleName == "연결된 화면 선택");
                Assert.True(screenList.Visible);
                Assert.Equal(6, screenList.Items.Count);
                screenList.SelectedIndex = 5;
                Assert.Contains(Controls(form), x => x.Text == MainForm.FormatMonitor(displays[5]));
                Assert.All(Controls(form).Where(x => x.Visible), control =>
                    Assert.True(form.ClientRectangle.Contains(Bounds(control)), $"{control.Text}: {Bounds(control)}"));

                var logout = Assert.Single(Controls(form).OfType<Button>(), x => x.Text == "로그아웃");
                var deviceCard = Assert.Single(Controls(form).OfType<BrandPanel>(), x => x.Visible);
                var oldCardWidth = deviceCard.Width;
                var oldRight = Bounds(logout).Right;
                form.Size = initial + resize;
                Assert.Equal(oldRight + resize.Width, Bounds(logout).Right);
                using (var bitmap = new Bitmap(deviceCard.Width, deviceCard.Height))
                {
                    deviceCard.DrawToBitmap(bitmap, deviceCard.ClientRectangle);
                    var center = bitmap.Width / 2;
                    Assert.True(bitmap.GetPixel(center, 0).R < 245, "Top edge");
                    Assert.True(bitmap.GetPixel(bitmap.Width - 1, 8).R < 245, "Right edge");
                    Assert.True(bitmap.GetPixel(center, bitmap.Height - 1).R < 245, "Bottom edge");
                    Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(oldCardWidth - 1, 8).ToArgb());
                }
                form.Size = initial - resize;
                Assert.Equal(initial, form.Size);
                Assert.False(form.VerticalScroll.Visible);
                Assert.False(form.HorizontalScroll.Visible);
                completed.SetResult();

                Rectangle Bounds(Control control) => form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            }
            catch (Exception error) { completed.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static IEnumerable<Control> Controls(Control control) =>
        control.Controls.Cast<Control>().SelectMany(child => Controls(child).Prepend(child));
}

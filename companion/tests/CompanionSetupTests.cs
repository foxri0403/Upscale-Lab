using UpscaleLab.Companion;
using Xunit;

public sealed class CompanionSetupTests
{
    [Fact]
    public void AutoLoginDoesNotSendStoredCredentialsToAnotherBuildsServer()
    {
        var session = new Login("access", "refresh", DateTime.UtcNow.AddHours(1), new(Guid.NewGuid(), "test@example.invalid", "test"));
        var saved = new Credentials("https://old.example.invalid", session, null, true, true);
        Assert.True(CompanionConfiguration.CanRestore(saved, saved.Server));
        Assert.False(CompanionConfiguration.CanRestore(saved, "http://127.0.0.1:5080"));
        Assert.False(CompanionConfiguration.CanRestore(saved with { Remember = false }, saved.Server));
        Assert.False(CompanionConfiguration.CanRestore(saved with { Session = null }, saved.Server));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void InstallerCopiesExecutableAndCreatesOnlySelectedShortcuts(bool desktop, bool startMenu)
    {
        var root = Path.Combine(Path.GetTempPath(), "UpscaleLabInstallerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var source = Path.Combine(root, "download.exe"); File.WriteAllText(source, "test executable");
            var links = new List<(string Path, string Target)>();
            var consentRecorded = false;
            var installer = new CompanionInstaller(Path.Combine(root, "app"), Path.Combine(root, "menu"), Path.Combine(root, "desktop"),
                (path, target) => links.Add((path, target)), () => consentRecorded = true);
            Assert.Throws<InvalidOperationException>(() => installer.Install(source, desktop, startMenu, false));
            Assert.False(Directory.Exists(Path.Combine(root, "app")));
            Assert.False(consentRecorded); Assert.Empty(links);
            var target = installer.Install(source, desktop, startMenu, true);
            Assert.True(consentRecorded);
            Assert.Equal("test executable", File.ReadAllText(target));
            Assert.Equal((desktop ? 1 : 0) + (startMenu ? 1 : 0), links.Count);
            Assert.All(links, link => Assert.Equal(target, link.Target));
            Assert.Equal(desktop, links.Any(link => link.Path == Path.Combine(root, "desktop", CompanionInstaller.ShortcutName)));
            Assert.Equal(startMenu, links.Any(link => link.Path == Path.Combine(root, "menu", CompanionInstaller.ShortcutName)));
            Assert.True(installer.IsInstalled(target));
        } finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ConsentIsRecordedWithUtcTimeAndDoesNotTransferToAnotherServer()
    {
        var root = Path.Combine(Path.GetTempPath(), "UpscaleLabConsentTests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "privacy.json");
        try {
            var consent = new PrivacyConsent(file, "https://one.example.invalid");
            Assert.False(consent.Accepted); consent.Accept(); Assert.True(consent.Accepted);
            Assert.False(new PrivacyConsent(file, "https://two.example.invalid").Accepted);
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            Assert.Equal(TimeSpan.Zero, json.RootElement.GetProperty("AcceptedAtUtc").GetDateTimeOffset().Offset);
            File.WriteAllText(file, "{}"); Assert.False(consent.Accepted);
        } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

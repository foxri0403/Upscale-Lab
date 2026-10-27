using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Api.Configuration;
using UpscaleLab.Api.Controllers;
using UpscaleLab.Api.Hubs;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Devices;
using UpscaleLab.Application.Wallpapers;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Auth;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Devices;
using UpscaleLab.Infrastructure.Wallpapers;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class DeviceUpdateHttpTests
{
    [Fact]
    public async Task ApplyWakesAuthenticatedAgentAndAppliedReportImmediatelyWakesBrowser()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var database = Guid.NewGuid().ToString();
        var jwt = new JwtOptions { Secret = "test-only-signing-secret-at-least-32-characters" };
        builder.Services.AddSingleton(TimeProvider.System); builder.Services.AddSingleton(jwt);
        builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(database));
        builder.Services.AddScoped<IActiveSessionValidator, ActiveSessionValidator>();
        builder.Services.AddScoped<IDeviceControlService, DeviceControlService>();
        builder.Services.AddScoped<IDeviceService, DeviceService>();
        builder.Services.AddScoped<IDeviceEvents, DeviceEvents>(); builder.Services.AddSingleton<DeviceChangeFeed>();
        builder.Services.AddScoped<IWallpaperService>(sp => { var storage = new WorkflowStore(); return new WallpaperService(sp.GetRequiredService<ApplicationDbContext>(), storage, storage); });
        builder.Services.AddClientAuthentication(jwt); builder.Services.AddSignalR();
        builder.Services.AddControllers().AddApplicationPart(typeof(AgentController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        Guid deviceId, displayId, assetId; string accountToken, deviceToken;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = new User { Email = "updates@test.invalid", Username = "updates", PasswordHash = "test-hash", IsEmailVerified = true };
            var session = new AuthSession { User = user, ExpiresAt = DateTime.UtcNow.AddDays(1),
                PasswordFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.PasswordHash))) };
            var device = WorkflowData.Device(user.Id); var asset = new WallpaperAsset { UserId = user.Id, ManifestJson = "{}" };
            db.AddRange(session, device, asset); await db.SaveChangesAsync(); deviceId = device.Id; assetId = asset.Id;
            var control = scope.ServiceProvider.GetRequiredService<IDeviceControlService>();
            deviceToken = (await control.RotateTokenAsync(user.Id, device.Id, CancellationToken.None)).Token;
            var state = await control.HeartbeatAsync(new(device.Id, user.Id), new(1, "Windows", "test", false, false,
                [new("test-monitor", "Test", 1920, 1080)]), CancellationToken.None);
            displayId = state.Displays.Single().Id; accountToken = new JwtTokenService(jwt).Create(user, session.Id).Token;
        }
        await app.StartAsync();
        try
        {
            using var browser = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(5) };
            using var agent = new HttpClient { BaseAddress = browser.BaseAddress, Timeout = TimeSpan.FromSeconds(5) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/devices/updates")).StatusCode);
            browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accountToken);
            agent.DefaultRequestHeaders.Authorization = browser.DefaultRequestHeaders.Authorization;
            Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("/api/agent/updates")).StatusCode);
            agent.DefaultRequestHeaders.Add("X-Device-Token", deviceToken);
            var initial = await agent.GetFromJsonAsync<JsonElement>("/api/agent/updates");
            var waiting = agent.GetFromJsonAsync<JsonElement>("/api/agent/updates?after=" + initial.GetProperty("cursor").GetString());
            var applied = await browser.PostAsJsonAsync($"/api/devices/{deviceId}/commands", new { requestId = Guid.NewGuid(), displayId, assetId, target = "Desktop" });
            Assert.Equal(HttpStatusCode.Accepted, applied.StatusCode);
            var command = await applied.Content.ReadFromJsonAsync<JsonElement>();
            Assert.NotEqual(initial.GetProperty("cursor").GetString(), (await waiting.WaitAsync(TimeSpan.FromSeconds(2))).GetProperty("cursor").GetString());
            var browserInitial = await browser.GetFromJsonAsync<JsonElement>("/api/devices/updates");
            var browserWaiting = browser.GetFromJsonAsync<JsonElement>("/api/devices/updates?after=" + browserInitial.GetProperty("cursor").GetString());
            var report = await agent.PostAsJsonAsync($"/api/agent/commands/{command.GetProperty("id").GetString()}/report", new { status = "Applied" });
            Assert.Equal(HttpStatusCode.OK, report.StatusCode);
            await browserWaiting.WaitAsync(TimeSpan.FromSeconds(2));
            var current = await browser.GetFromJsonAsync<JsonElement>($"/api/devices/{deviceId}/state");
            Assert.Equal(assetId.ToString(), current.GetProperty("displays")[0].GetProperty("wallpapers")[0].GetProperty("reportedAssetId").GetString());
        }
        finally { await app.StopAsync(); }
    }
}

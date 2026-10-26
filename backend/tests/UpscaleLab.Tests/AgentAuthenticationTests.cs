using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Api.Configuration;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Devices;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Auth;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Devices;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class AgentAuthenticationTests
{
    [Theory]
    [InlineData("expired")]
    [InlineData("password-changed")]
    [InlineData("unverified")]
    [InlineData("wrong-owner")]
    public async Task ActiveSessionValidator_RejectsInvalidAccountState(string reason)
    {
        await using var db = WorkflowData.Db();
        var clock = new WorkflowClock();
        var user = new User { Email = "session@test.invalid", Username = "session", PasswordHash = "original-hash", IsEmailVerified = true };
        var session = new AuthSession { User = user, ExpiresAt = clock.Time.AddDays(1).UtcDateTime,
            PasswordFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.PasswordHash))) };
        db.AuthSessions.Add(session); await db.SaveChangesAsync();
        var validator = new ActiveSessionValidator(db, clock);
        Assert.True(await validator.IsActiveAsync(user.Id, session.Id, CancellationToken.None));
        if (reason == "expired") clock.Time = clock.Time.AddDays(2);
        if (reason == "password-changed") user.PasswordHash = "new-hash";
        if (reason == "unverified") user.IsEmailVerified = false;
        await db.SaveChangesAsync();
        Assert.False(await validator.IsActiveAsync(reason == "wrong-owner" ? Guid.NewGuid() : user.Id, session.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(true, true, false, false, true)]
    [InlineData(false, true, false, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, false, true, false)]
    public async Task AgentRequiresDeviceCredentialAndActiveMatchingAccount(bool accountHeader, bool deviceHeader,
        bool otherAccount, bool revoked, bool expected)
    {
        var options = new JwtOptions { Secret = "test-only-signing-secret-at-least-32-characters" };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddDbContext<ApplicationDbContext>(builder => builder.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddScoped<IActiveSessionValidator, ActiveSessionValidator>();
        services.AddScoped<IDeviceControlService, DeviceControlService>();
        services.AddClientAuthentication(options);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var owner = new User { Email = "owner@test.invalid", Username = "owner", PasswordHash = "hash", IsEmailVerified = true };
        var login = otherAccount ? new User { Email = "other@test.invalid", Username = "other", PasswordHash = "hash", IsEmailVerified = true } : owner;
        var session = new AuthSession { User = login, ExpiresAt = DateTime.UtcNow.AddDays(1),
            PasswordFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(login.PasswordHash))),
            RevokedAt = revoked ? DateTime.UtcNow : null };
        var device = WorkflowData.Device(owner.Id);
        db.Users.Add(owner); db.AuthSessions.Add(session); db.Devices.Add(device);
        await db.SaveChangesAsync();
        var key = await scope.ServiceProvider.GetRequiredService<IDeviceControlService>().RotateTokenAsync(owner.Id, device.Id, CancellationToken.None);
        var token = new JwtTokenService(options).Create(login, session.Id).Token;
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (accountHeader) context.Request.Headers.Authorization = "Bearer " + token;
        if (deviceHeader) context.Request.Headers["X-Device-Token"] = key.Token;
        var result = await context.AuthenticateAsync(DeviceAgentHandler.SchemeName);
        Assert.Equal(expected, result.Succeeded);
        if (expected) Assert.Equal(device.Id.ToString(), result.Principal!.FindFirst("device_id")!.Value);
    }
}

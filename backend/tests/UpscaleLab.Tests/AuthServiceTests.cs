using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Common;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Auth;
using UpscaleLab.Infrastructure.Database;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task Register_HashesPasswordAndReturnsToken()
    {
        await using var dbContext = CreateDbContext();
        var service = new AuthService(dbContext, new PasswordHasher(), new FakeJwtTokenService());

        var result = await service.RegisterAsync(
            new RegisterRequest("USER@example.com", "tester", "correct-horse-battery"),
            CancellationToken.None);

        var stored = await dbContext.Users.SingleAsync();
        Assert.Equal("user@example.com", stored.Email);
        Assert.NotEqual("correct-horse-battery", stored.PasswordHash);
        Assert.StartsWith("$2", stored.PasswordHash);
        Assert.Equal("test-token", result.AccessToken);
        Assert.NotNull(stored.Setting);
    }

    [Fact]
    public async Task Login_WithWrongPassword_IsRejected()
    {
        await using var dbContext = CreateDbContext();
        var service = new AuthService(dbContext, new PasswordHasher(), new FakeJwtTokenService());
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", "correct-horse-battery"),
            CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.LoginAsync(
            new LoginRequest("user@example.com", "wrong-password"),
            CancellationToken.None));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FakeJwtTokenService : IJwtTokenService
    {
        public (string Token, DateTime ExpiresAt) Create(User user) =>
            ("test-token", DateTime.UtcNow.AddHours(1));
    }
}

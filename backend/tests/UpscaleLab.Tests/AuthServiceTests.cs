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
    private const string ValidPassword = "Correct-horse1!";

    [Fact]
    public async Task Register_HashesPasswordAndReturnsToken()
    {
        await using var dbContext = CreateDbContext();
        var service = new AuthService(dbContext, new PasswordHasher(), new FakeJwtTokenService());

        var result = await service.RegisterAsync(
            new RegisterRequest("USER@example.com", "tester", ValidPassword),
            CancellationToken.None);

        var stored = await dbContext.Users.SingleAsync();
        Assert.Equal("user@example.com", stored.Email);
        Assert.NotEqual(ValidPassword, stored.PasswordHash);
        Assert.StartsWith("$2", stored.PasswordHash);
        Assert.True(new PasswordHasher().Verify(ValidPassword, stored.PasswordHash));
        Assert.Equal("test-token", result.AccessToken);
        Assert.NotNull(stored.Setting);
    }

    [Theory]
    [InlineData("Short1!")]
    [InlineData("lowercase1!")]
    [InlineData("NoDigitsHere!")]
    [InlineData("NoSpecial123")]
    public async Task Register_WithPasswordThatDoesNotMeetPolicy_IsRejected(string password)
    {
        await using var dbContext = CreateDbContext();
        var service = new AuthService(dbContext, new PasswordHasher(), new FakeJwtTokenService());

        var exception = await Assert.ThrowsAsync<ValidationException>(() => service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", password),
            CancellationToken.None));

        Assert.Equal(PasswordPolicy.ErrorMessage, exception.Message);
        Assert.Empty(dbContext.Users);
    }

    [Fact]
    public async Task Login_WithCorrectPassword_ReturnsToken()
    {
        await using var dbContext = CreateDbContext();
        var service = new AuthService(dbContext, new PasswordHasher(), new FakeJwtTokenService());
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);

        var result = await service.LoginAsync(
            new LoginRequest("USER@example.com", ValidPassword),
            CancellationToken.None);

        Assert.Equal("test-token", result.AccessToken);
        Assert.Equal("user@example.com", result.User.Email);
    }

    [Fact]
    public async Task Login_WithWrongPassword_IsRejected()
    {
        await using var dbContext = CreateDbContext();
        var service = new AuthService(dbContext, new PasswordHasher(), new FakeJwtTokenService());
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
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

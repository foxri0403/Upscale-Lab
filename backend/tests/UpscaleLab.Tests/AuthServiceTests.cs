using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Common;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Auth;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Email;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class AuthServiceTests
{
    private const string ValidPassword = "Correct-horse1!";

    [Fact]
    public async Task Register_HashesPasswordAndSendsVerificationCode()
    {
        await using var dbContext = CreateDbContext();
        var (service, emailSender) = CreateService(dbContext);

        var result = await service.RegisterAsync(
            new RegisterRequest("USER@example.com", "tester", ValidPassword),
            CancellationToken.None);

        var stored = await dbContext.Users.SingleAsync();
        Assert.Equal("user@example.com", stored.Email);
        Assert.NotEqual(ValidPassword, stored.PasswordHash);
        Assert.StartsWith("$2", stored.PasswordHash);
        Assert.True(new PasswordHasher().Verify(ValidPassword, stored.PasswordHash));
        Assert.False(stored.IsEmailVerified);
        Assert.NotEqual("000001", stored.EmailVerificationCodeHash);
        Assert.NotNull(stored.EmailVerificationCodeExpiresAt);
        Assert.True(result.RequiresEmailVerification);
        Assert.False(result.User.IsEmailVerified);
        Assert.Equal("000001", Assert.Single(emailSender.SentMessages).Code);
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
        var (service, emailSender) = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", password),
            CancellationToken.None));

        Assert.Equal(PasswordPolicy.ErrorMessage, exception.Message);
        Assert.Empty(dbContext.Users);
        Assert.Empty(emailSender.SentMessages);
    }

    [Fact]
    public async Task VerifyEmail_WithValidCode_VerifiesUserAndReturnsToken()
    {
        await using var dbContext = CreateDbContext();
        var (service, _) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);

        var result = await service.VerifyEmailAsync(
            new VerifyEmailRequest("USER@example.com", "000001"),
            CancellationToken.None);

        var stored = await dbContext.Users.SingleAsync();
        Assert.True(stored.IsEmailVerified);
        Assert.NotNull(stored.EmailVerifiedAt);
        Assert.Null(stored.EmailVerificationCodeHash);
        Assert.Null(stored.EmailVerificationCodeExpiresAt);
        Assert.Equal("test-token", result.AccessToken);
        Assert.True(result.User.IsEmailVerified);
    }

    [Fact]
    public async Task VerifyEmail_WithExpiredCode_IsRejected()
    {
        await using var dbContext = CreateDbContext();
        var (service, _) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);
        var user = await dbContext.Users.SingleAsync();
        user.EmailVerificationCodeExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => service.VerifyEmailAsync(
            new VerifyEmailRequest("user@example.com", "000001"),
            CancellationToken.None));
    }

    [Fact]
    public async Task VerifyEmail_AfterMaximumFailedAttempts_InvalidatesCode()
    {
        await using var dbContext = CreateDbContext();
        var (service, _) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Assert.ThrowsAsync<ValidationException>(() => service.VerifyEmailAsync(
                new VerifyEmailRequest("user@example.com", "999999"),
                CancellationToken.None));
        }

        var stored = await dbContext.Users.SingleAsync();
        Assert.Null(stored.EmailVerificationCodeHash);
        await Assert.ThrowsAsync<ValidationException>(() => service.VerifyEmailAsync(
            new VerifyEmailRequest("user@example.com", "000001"),
            CancellationToken.None));
    }

    [Fact]
    public async Task ResendVerificationEmail_AfterCooldown_ReplacesCode()
    {
        await using var dbContext = CreateDbContext();
        var (service, emailSender) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);
        var user = await dbContext.Users.SingleAsync();
        var originalHash = user.EmailVerificationCodeHash;
        user.EmailVerificationCodeSentAt = DateTime.UtcNow.AddMinutes(-2);
        await dbContext.SaveChangesAsync();

        await service.ResendVerificationEmailAsync(
            new ResendVerificationEmailRequest("user@example.com"),
            CancellationToken.None);

        Assert.Equal(2, emailSender.SentMessages.Count);
        Assert.Equal("000002", emailSender.SentMessages[1].Code);
        Assert.NotEqual(originalHash, user.EmailVerificationCodeHash);
    }

    [Fact]
    public async Task ResendVerificationEmail_WithinCooldown_DoesNotSendAgain()
    {
        await using var dbContext = CreateDbContext();
        var (service, emailSender) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);

        await service.ResendVerificationEmailAsync(
            new ResendVerificationEmailRequest("user@example.com"),
            CancellationToken.None);

        Assert.Single(emailSender.SentMessages);
    }

    [Fact]
    public async Task Login_BeforeEmailVerification_IsRejected()
    {
        await using var dbContext = CreateDbContext();
        var (service, _) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.LoginAsync(
            new LoginRequest("user@example.com", ValidPassword),
            CancellationToken.None));
    }

    [Fact]
    public async Task Login_AfterEmailVerification_ReturnsToken()
    {
        await using var dbContext = CreateDbContext();
        var (service, _) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);
        await service.VerifyEmailAsync(
            new VerifyEmailRequest("user@example.com", "000001"),
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
        var (service, _) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.LoginAsync(
            new LoginRequest("user@example.com", "wrong-password"),
            CancellationToken.None));
    }

    private static (AuthService Service, FakeEmailSender EmailSender) CreateService(
        ApplicationDbContext dbContext)
    {
        var emailSender = new FakeEmailSender();
        var service = new AuthService(
            dbContext,
            new PasswordHasher(),
            new FakeJwtTokenService(),
            emailSender,
            new FakeVerificationCodeProtector(),
            new EmailVerificationOptions
            {
                CodeExpirationMinutes = 10,
                ResendCooldownSeconds = 60,
                MaxFailedAttempts = 5
            });
        return (service, emailSender);
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

    private sealed class FakeVerificationCodeProtector : IEmailVerificationCodeProtector
    {
        private int sequence;

        public string Generate() => (++sequence).ToString("D6");

        public string Hash(string email, string code) => $"hash:{email.ToLowerInvariant()}:{code}";

        public bool Verify(string email, string code, string expectedHash) =>
            Hash(email, code) == expectedHash;
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<SentMessage> SentMessages { get; } = [];

        public Task SendVerificationCodeAsync(
            string recipientEmail,
            string recipientName,
            string code,
            int expiresInMinutes,
            CancellationToken cancellationToken)
        {
            SentMessages.Add(new SentMessage(recipientEmail, recipientName, code, expiresInMinutes));
            return Task.CompletedTask;
        }
    }

    private sealed record SentMessage(
        string RecipientEmail,
        string RecipientName,
        string Code,
        int ExpiresInMinutes);
}

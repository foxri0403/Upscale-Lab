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
    public async Task Register_HashesPasswordAndStartsCognitoEmailVerification()
    {
        await using var dbContext = CreateDbContext();
        var (service, provider) = CreateService(dbContext);

        var result = await service.RegisterAsync(
            new RegisterRequest("USER@example.com", "tester", ValidPassword),
            CancellationToken.None);

        var stored = await dbContext.Users.SingleAsync();
        Assert.Equal("user@example.com", stored.Email);
        Assert.NotEqual(ValidPassword, stored.PasswordHash);
        Assert.StartsWith("$2", stored.PasswordHash);
        Assert.True(new PasswordHasher().Verify(ValidPassword, stored.PasswordHash));
        Assert.False(stored.IsEmailVerified);
        Assert.NotNull(stored.EmailVerificationSentAt);
        Assert.True(result.RequiresEmailVerification);
        Assert.False(result.User.IsEmailVerified);
        Assert.Equal(("user@example.com", ValidPassword), Assert.Single(provider.SignUps));
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
        var (service, provider) = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", password),
            CancellationToken.None));

        Assert.Equal(PasswordPolicy.ErrorMessage, exception.Message);
        Assert.Empty(dbContext.Users);
        Assert.Empty(provider.SignUps);
    }

    [Fact]
    public async Task VerifyEmail_WithValidCode_ConfirmsCognitoUserAndReturnsToken()
    {
        await using var dbContext = CreateDbContext();
        var (service, provider) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);

        var result = await service.VerifyEmailAsync(
            new VerifyEmailRequest("USER@example.com", "000001"),
            CancellationToken.None);

        var stored = await dbContext.Users.SingleAsync();
        Assert.True(stored.IsEmailVerified);
        Assert.NotNull(stored.EmailVerifiedAt);
        Assert.Null(stored.EmailVerificationSentAt);
        Assert.Equal(("user@example.com", "000001"), Assert.Single(provider.Confirmations));
        Assert.Equal("test-token", result.AccessToken);
        Assert.True(result.User.IsEmailVerified);
    }

    [Fact]
    public async Task VerifyEmail_WhenCognitoRejectsCode_DoesNotVerifyUser()
    {
        await using var dbContext = CreateDbContext();
        var (service, provider) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);
        provider.ConfirmationException = new ValidationException("invalid code");

        await Assert.ThrowsAsync<ValidationException>(() => service.VerifyEmailAsync(
            new VerifyEmailRequest("user@example.com", "999999"),
            CancellationToken.None));

        Assert.False((await dbContext.Users.SingleAsync()).IsEmailVerified);
    }

    [Fact]
    public async Task ResendVerificationEmail_AfterCooldown_RequestsNewCognitoCode()
    {
        await using var dbContext = CreateDbContext();
        var (service, provider) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);
        var user = await dbContext.Users.SingleAsync();
        user.EmailVerificationSentAt = DateTime.UtcNow.AddMinutes(-2);
        await dbContext.SaveChangesAsync();

        await service.ResendVerificationEmailAsync(
            new ResendVerificationEmailRequest("user@example.com"),
            CancellationToken.None);

        Assert.Equal("user@example.com", Assert.Single(provider.Resends));
        Assert.True(user.EmailVerificationSentAt > DateTime.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public async Task ResendVerificationEmail_WithinCooldown_DoesNotSendAgain()
    {
        await using var dbContext = CreateDbContext();
        var (service, provider) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);

        await service.ResendVerificationEmailAsync(
            new ResendVerificationEmailRequest("user@example.com"),
            CancellationToken.None);

        Assert.Empty(provider.Resends);
    }

    [Fact]
    public async Task FindId_ForVerifiedUser_ReturnsFullUsername()
    {
        await using var dbContext = CreateDbContext();
        var (service, _) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);
        await service.VerifyEmailAsync(
            new VerifyEmailRequest("user@example.com", "000001"),
            CancellationToken.None);

        var result = await service.FindIdAsync(
            new FindIdRequest(" USER@example.com "),
            CancellationToken.None);

        Assert.Equal("tester", result.Username);
    }

    [Fact]
    public async Task FindId_ForUnverifiedUser_DoesNotReturnUsername()
    {
        await using var dbContext = CreateDbContext();
        var (service, _) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);

        var result = await service.FindIdAsync(
            new FindIdRequest("user@example.com"),
            CancellationToken.None);

        Assert.Null(result.Username);
    }

    [Fact]
    public async Task StartPasswordReset_ForVerifiedUser_RequestsCognitoCode()
    {
        await using var dbContext = CreateDbContext();
        var (service, provider) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);
        await service.VerifyEmailAsync(
            new VerifyEmailRequest("user@example.com", "000001"),
            CancellationToken.None);

        await service.StartPasswordResetAsync(
            new PasswordResetStartRequest("USER@example.com"),
            CancellationToken.None);

        Assert.Equal("user@example.com", Assert.Single(provider.PasswordResetStarts));
    }

    [Fact]
    public async Task StartPasswordReset_ForUnknownUser_DoesNotRevealAccount()
    {
        await using var dbContext = CreateDbContext();
        var (service, provider) = CreateService(dbContext);

        await service.StartPasswordResetAsync(
            new PasswordResetStartRequest("missing@example.com"),
            CancellationToken.None);

        Assert.Empty(provider.PasswordResetStarts);
    }

    [Fact]
    public async Task ConfirmPasswordReset_ReplacesStoredBcryptHash()
    {
        const string newPassword = "Changed-horse2!";
        await using var dbContext = CreateDbContext();
        var (service, provider) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);
        await service.VerifyEmailAsync(
            new VerifyEmailRequest("user@example.com", "000001"),
            CancellationToken.None);
        var before = (await dbContext.Users.SingleAsync()).UpdatedAt;

        await service.ConfirmPasswordResetAsync(
            new PasswordResetConfirmRequest("USER@example.com", "123456", newPassword),
            CancellationToken.None);

        var stored = await dbContext.Users.SingleAsync();
        Assert.False(new PasswordHasher().Verify(ValidPassword, stored.PasswordHash));
        Assert.True(new PasswordHasher().Verify(newPassword, stored.PasswordHash));
        Assert.StartsWith("$2", stored.PasswordHash);
        Assert.True(stored.UpdatedAt >= before);
        Assert.Equal(
            ("user@example.com", "123456", newPassword),
            Assert.Single(provider.PasswordResetConfirmations));
    }

    [Fact]
    public async Task ConfirmPasswordReset_WithSamePassword_IsAcceptedAndRehashed()
    {
        await using var dbContext = CreateDbContext();
        var (service, provider) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);
        await service.VerifyEmailAsync(
            new VerifyEmailRequest("user@example.com", "000001"),
            CancellationToken.None);
        var previousHash = (await dbContext.Users.SingleAsync()).PasswordHash;

        await service.ConfirmPasswordResetAsync(
            new PasswordResetConfirmRequest("user@example.com", "123456", ValidPassword),
            CancellationToken.None);

        var stored = await dbContext.Users.SingleAsync();
        Assert.NotEqual(previousHash, stored.PasswordHash);
        Assert.True(new PasswordHasher().Verify(ValidPassword, stored.PasswordHash));
        Assert.Equal(
            ("user@example.com", "123456", ValidPassword),
            Assert.Single(provider.PasswordResetConfirmations));
    }

    [Fact]
    public async Task ConfirmPasswordReset_WhenCognitoRejectsCode_DoesNotChangeStoredHash()
    {
        const string newPassword = "Changed-horse2!";
        await using var dbContext = CreateDbContext();
        var (service, provider) = CreateService(dbContext);
        await service.RegisterAsync(
            new RegisterRequest("user@example.com", "tester", ValidPassword),
            CancellationToken.None);
        await service.VerifyEmailAsync(
            new VerifyEmailRequest("user@example.com", "000001"),
            CancellationToken.None);
        provider.PasswordResetConfirmationException = new ValidationException("invalid code");

        await Assert.ThrowsAsync<ValidationException>(() => service.ConfirmPasswordResetAsync(
            new PasswordResetConfirmRequest("user@example.com", "999999", newPassword),
            CancellationToken.None));

        var stored = await dbContext.Users.SingleAsync();
        Assert.True(new PasswordHasher().Verify(ValidPassword, stored.PasswordHash));
        Assert.False(new PasswordHasher().Verify(newPassword, stored.PasswordHash));
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
    public async Task Login_WithUsername_ReturnsToken()
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
            new LoginRequest("tester", ValidPassword),
            CancellationToken.None);

        Assert.Equal("test-token", result.AccessToken);
        Assert.Equal("tester", result.User.Username);
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

    private static (AuthService Service, FakeEmailVerificationProvider Provider) CreateService(
        ApplicationDbContext dbContext)
    {
        var provider = new FakeEmailVerificationProvider();
        var options = new CognitoOptions
        {
            ResendCooldownSeconds = 60
        };
        var service = new AuthService(
            dbContext,
            new PasswordHasher(),
            new FakeJwtTokenService(),
            provider,
            provider,
            options);
        return (service, provider);
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

    private sealed class FakeEmailVerificationProvider
        : IEmailVerificationProvider, IPasswordRecoveryProvider
    {
        public List<(string Email, string Password)> SignUps { get; } = [];
        public List<(string Email, string Code)> Confirmations { get; } = [];
        public List<string> Resends { get; } = [];
        public List<string> PasswordResetStarts { get; } = [];
        public List<(string Email, string Code, string NewPassword)> PasswordResetConfirmations { get; } = [];
        public Exception? ConfirmationException { get; set; }
        public Exception? PasswordResetConfirmationException { get; set; }

        public Task<DateTime> SignUpAsync(
            string email,
            string password,
            CancellationToken cancellationToken)
        {
            SignUps.Add((email, password));
            return Task.FromResult(DateTime.UtcNow.AddHours(24));
        }

        public Task ConfirmSignUpAsync(
            string email,
            string code,
            CancellationToken cancellationToken)
        {
            if (ConfirmationException is not null)
            {
                throw ConfirmationException;
            }

            Confirmations.Add((email, code));
            return Task.CompletedTask;
        }

        public Task ResendConfirmationCodeAsync(string email, CancellationToken cancellationToken)
        {
            Resends.Add(email);
            return Task.CompletedTask;
        }

        public Task StartPasswordResetAsync(string email, CancellationToken cancellationToken)
        {
            PasswordResetStarts.Add(email);
            return Task.CompletedTask;
        }

        public Task ConfirmPasswordResetAsync(
            string email,
            string code,
            string newPassword,
            CancellationToken cancellationToken)
        {
            if (PasswordResetConfirmationException is not null)
            {
                throw PasswordResetConfirmationException;
            }

            PasswordResetConfirmations.Add((email, code, newPassword));
            return Task.CompletedTask;
        }
    }
}

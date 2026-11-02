using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Common;
using UpscaleLab.Domain.Entities;
using Xunit;

namespace UpscaleLab.Tests;

public sealed partial class AuthServiceTests
{
    [Fact]
    public async Task Registration_CreatesMemberAndSessionOnlyAfterVerifiedSignedConsent()
    {
        await using var db = CreateDbContext();
        var clock = new TestClock();
        var (service, _) = CreateService(db, clock);
        var registration = await service.RegisterAsync(new("person@example.com", "person", ValidPassword), default);
        Assert.Empty(db.Users); Assert.Empty(db.UserSettings); Assert.Empty(db.AuthSessions);
        Assert.Equal(clock.Now, registration.VerificationCodeSentAt);
        Assert.Equal(clock.Now.AddMinutes(15), registration.VerificationCodeExpiresAt);
        var challenge = await service.VerifyEmailAsync(new("person@example.com", "000001"), default);
        Assert.Empty(db.Users); Assert.Empty(db.AuthSessions);
        Assert.NotEqual(challenge.Token, (await db.PendingRegistrations.SingleAsync()).ConsentTokenHash);

        var consent = new AcceptRegistrationTermsRequest(challenge.Email, challenge.Token, challenge.TermsVersion, true, true, "person");
        await Assert.ThrowsAsync<ValidationException>(() => service.AcceptRegistrationTermsAsync(consent with { AcceptTerms = false }, default));
        await Assert.ThrowsAsync<ValidationException>(() => service.AcceptRegistrationTermsAsync(consent with { ConfirmSignature = false }, default));
        await Assert.ThrowsAsync<ValidationException>(() => service.AcceptRegistrationTermsAsync(consent with { Signature = "someone" }, default));
        await Assert.ThrowsAsync<ValidationException>(() => service.AcceptRegistrationTermsAsync(consent with { TermsVersion = "old" }, default));
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.AcceptRegistrationTermsAsync(consent with { Token = new string('0', 64) }, default));
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.AcceptRegistrationTermsAsync(consent with { Email = "other@example.com" }, default));
        Assert.Empty(db.Users); Assert.Empty(db.AuthSessions);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.LoginAsync(new("person", ValidPassword), default));

        var session = await service.AcceptRegistrationTermsAsync(consent, default);
        var user = await db.Users.SingleAsync();
        Assert.Equal("test-token", session.AccessToken);
        Assert.Equal(clock.Now, user.CopyrightTermsAcceptedAt);
        Assert.Equal(CopyrightTerms.Version, user.CopyrightTermsVersion);
        Assert.Equal(CopyrightTerms.DocumentHash, user.CopyrightTermsDocumentHash);
        Assert.Equal("person", user.CopyrightSignature);
        Assert.True(user.IsEmailVerified); Assert.NotNull(user.EmailVerifiedAt);
        Assert.Single(db.UserSettings); Assert.Single(db.AuthSessions); Assert.Empty(db.PendingRegistrations);
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.AcceptRegistrationTermsAsync(consent, default));
        Assert.Single(db.Users); Assert.Single(db.AuthSessions);
    }

    [Fact]
    public async Task ExpiredConsent_RequiresOriginalCredentialsToResumeAndRotatesTicket()
    {
        await using var db = CreateDbContext();
        var clock = new TestClock();
        var (service, _) = CreateService(db, clock);
        var request = new RegisterRequest("person@example.com", "person", ValidPassword);
        await service.RegisterAsync(request, default);
        var original = await service.VerifyEmailAsync(new(request.Email, "000001"), default);
        clock.Now = clock.Now.AddMinutes(15);
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.AcceptRegistrationTermsAsync(
            new(request.Email, original.Token, original.TermsVersion, true, true, "person"), default));
        await Assert.ThrowsAsync<ConflictException>(() => service.RegisterAsync(request with { Password = "Other-password1!" }, default));
        var resumed = await service.RegisterAsync(request, default);
        Assert.False(resumed.RequiresEmailVerification);
        Assert.NotNull(resumed.Registration); Assert.NotEqual(original.Token, resumed.Registration.Token);
        Assert.Empty(db.Users); Assert.Empty(db.AuthSessions);
        await service.AcceptRegistrationTermsAsync(new(request.Email, resumed.Registration.Token,
            resumed.Registration.TermsVersion, true, true, "person"), default);
        Assert.Single(db.Users);
    }

    [Fact]
    public async Task Cleanup_ExpiresVerifiedPendingStateWithoutDeletingAnyMember()
    {
        await using var db = CreateDbContext();
        var clock = new TestClock();
        var (service, provider) = CreateService(db, clock);
        db.Users.Add(new User { Email = "legacy@example.com", Username = "legacy", IsEmailVerified = false, CreatedAt = clock.Now.AddDays(-10) });
        await db.SaveChangesAsync();
        await service.RegisterAsync(new("pending@example.com", "pending", ValidPassword), default);
        await service.VerifyEmailAsync(new("pending@example.com", "000001"), default);
        clock.Now = clock.Now.AddHours(24);
        await service.DeleteExpiredRegistrationsAndSessionsAsync(default);
        Assert.Empty(db.PendingRegistrations);
        Assert.Equal("legacy@example.com", (await db.Users.SingleAsync()).Email);
        Assert.Equal("pending@example.com", Assert.Single(provider.Deletions));
    }

    [Fact]
    public async Task Resend_ReturnsActualDeadlineAndCooldownCannotExtendItWithoutSending()
    {
        await using var db = CreateDbContext();
        var clock = new TestClock();
        var (service, provider) = CreateService(db, clock);
        var registration = await service.RegisterAsync(new("person@example.com", "person", ValidPassword), default);
        var early = await service.ResendVerificationEmailAsync(new(registration.Email), default);
        Assert.Equal(registration.VerificationCodeExpiresAt, early.VerificationCodeExpiresAt);
        Assert.Empty(provider.Resends);
        clock.Now = clock.Now.AddMinutes(1);
        var sent = await service.ResendVerificationEmailAsync(new(registration.Email), default);
        Assert.Equal(clock.Now, sent.VerificationCodeSentAt);
        Assert.Equal(clock.Now.AddMinutes(15), sent.VerificationCodeExpiresAt);
        Assert.Equal(clock.Now.AddMinutes(1), sent.ResendAvailableAt);
        Assert.Single(provider.Resends); Assert.Empty(db.Users);
        var unknown = await service.ResendVerificationEmailAsync(new("unknown@example.com"), default);
        Assert.Null(unknown.VerificationCodeExpiresAt);
    }

    [Theory]
    [InlineData("한글")]
    [InlineData("user_name")]
    [InlineData("user-name")]
    public async Task Register_RejectsIdsOutsideLettersAndDigitsBeforeSendingEmail(string username)
    {
        await using var db = CreateDbContext();
        var (service, provider) = CreateService(db);
        await Assert.ThrowsAsync<ValidationException>(() => service.RegisterAsync(new("person@example.com", username, ValidPassword), default));
        Assert.Empty(db.Users); Assert.Empty(db.PendingRegistrations); Assert.Empty(provider.SignUps);
    }

    [Theory]
    [InlineData("user123")]
    [InlineData("12345")]
    public async Task Register_AllowsLettersAndDigits(string username)
    {
        await using var db = CreateDbContext();
        var (service, _) = CreateService(db);
        var result = await service.RegisterAsync(new("person@example.com", username, ValidPassword), default);
        Assert.Equal(username, result.Username);
        Assert.Equal(64, result.CancellationToken.Length);
        Assert.NotEqual(result.CancellationToken, (await db.PendingRegistrations.SingleAsync()).CancellationTokenHash);
        Assert.Empty(db.Users);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelRegistration_RemovesPendingAndExternalIdentityAndAllowsFreshDetails(bool verified)
    {
        await using var db = CreateDbContext();
        var (service, provider) = CreateService(db);
        var initial = await service.RegisterAsync(new("person@example.com", "person123", ValidPassword), default);
        if (verified) await service.VerifyEmailAsync(new(initial.Email, "000001"), default);
        await service.CancelRegistrationAsync(new(initial.Email, initial.CancellationToken), default);
        Assert.Empty(db.PendingRegistrations); Assert.Empty(db.Users); Assert.Empty(db.AuthSessions);
        Assert.Equal(initial.Email, Assert.Single(provider.Deletions));
        await service.CancelRegistrationAsync(new(initial.Email, initial.CancellationToken), default);
        var replacement = await service.RegisterAsync(new(initial.Email, "newName456", "Changed-horse2!"), default);
        Assert.Equal("newName456", replacement.Username);
        Assert.NotEqual(initial.CancellationToken, replacement.CancellationToken);
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.CancelRegistrationAsync(new(initial.Email, initial.CancellationToken), default));
        Assert.Single(db.PendingRegistrations);
    }

    [Fact]
    public async Task CancelRegistration_RequiresRegistrationControlAndRetainsPendingIfProviderFails()
    {
        await using var db = CreateDbContext();
        var (service, provider) = CreateService(db);
        var registration = await service.RegisterAsync(new("person@example.com", "person", ValidPassword), default);
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.CancelRegistrationAsync(new(registration.Email), default));
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.CancelRegistrationAsync(new(registration.Email, new string('0',64), "wrong"), default));
        Assert.Single(db.PendingRegistrations); Assert.Empty(provider.Deletions);
        provider.RefusePendingDelete = true;
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => service.CancelRegistrationAsync(new(registration.Email, registration.CancellationToken), default));
        Assert.Single(db.PendingRegistrations);
        provider.RefusePendingDelete = false;
        provider.DeleteFailure = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelRegistrationAsync(new(registration.Email, registration.CancellationToken), default));
        Assert.Single(db.PendingRegistrations);
        provider.DeleteFailure = false;
        // Browsers opened before the cancellation credential was introduced can
        // prove ownership with the original password instead.
        (await db.PendingRegistrations.SingleAsync()).CancellationTokenHash = null;
        await db.SaveChangesAsync();
        await service.CancelRegistrationAsync(new(registration.Email, Password: ValidPassword), default);
        Assert.Empty(db.PendingRegistrations);
    }

    [Fact]
    public async Task CancelRegistration_NeverDeletesCompletedMemberOrItsExternalIdentity()
    {
        await using var db = CreateDbContext();
        var (service, provider) = CreateService(db);
        var registration = await service.RegisterAsync(new("person@example.com", "person", ValidPassword), default);
        await VerifyAndAcceptAsync(service, new(registration.Email,"000001"), default);
        await service.CancelRegistrationAsync(new(registration.Email, registration.CancellationToken), default);
        Assert.Single(db.Users); Assert.Single(db.AuthSessions); Assert.Empty(provider.Deletions);
        db.PendingRegistrations.Add(new PendingRegistration { Email=registration.Email, Username="shadow", PasswordHash="unused" });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => service.CancelRegistrationAsync(new(registration.Email, registration.CancellationToken), default));
        Assert.Single(db.Users); Assert.Single(db.PendingRegistrations); Assert.Empty(provider.Deletions);
    }
}

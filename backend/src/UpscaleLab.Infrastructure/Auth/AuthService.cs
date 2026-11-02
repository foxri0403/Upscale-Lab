using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Common;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Auth;

public sealed class AuthService(
    ApplicationDbContext dbContext,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    IEmailVerificationProvider emailVerificationProvider,
    IPasswordRecoveryProvider passwordRecoveryProvider,
    CognitoOptions cognitoOptions,
    IUnverifiedAccountCleaner? accountCleaner = null,
    TimeProvider? timeProvider = null,
    ILogger<AuthService>? logger = null) : IAuthService
{
    private DateTime Now => (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    public static readonly TimeSpan VerificationLifetime = TimeSpan.FromMinutes(15);
    private const string InvalidVerificationCodeMessage =
        "인증 코드가 올바르지 않거나 만료되었습니다. 새 코드를 요청해 주세요.";
    private const string InvalidPasswordResetMessage =
        "재설정 코드가 올바르지 않거나 만료되었습니다. 새 코드를 요청해 주세요.";

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var username = request.Username.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(username, "^[A-Za-z0-9]{2,50}$"))
            throw new ValidationException("아이디는 영문과 숫자만 이용 가능합니다.");
        if (!PasswordPolicy.IsSatisfiedBy(request.Password))
            throw new ValidationException(PasswordPolicy.ErrorMessage);

        var staleEmails = await dbContext.PendingRegistrations.AsNoTracking()
            .Where(x => x.ExpiresAt <= Now && (x.Email == email || x.Username == username))
            .Select(x => x.Email).ToListAsync(cancellationToken);
        foreach (var stale in staleEmails) await DeleteExpiredPendingAsync(stale, cancellationToken);

        return await AuthUserTransaction.RunAsync(dbContext, email, async () =>
        {
            if (dbContext.Database.IsRelational())
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({"registration-username:" + username}, 0))", cancellationToken);
            if (await dbContext.Users.AnyAsync(x => x.Email == email || x.Username == username, cancellationToken))
                throw new ConflictException("이미 사용 중인 이메일 또는 아이디입니다.");
            var pending = await dbContext.PendingRegistrations.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
            if (pending is not null)
            {
                if (pending.ExpiresAt <= Now || pending.Username != username ||
                    !passwordHasher.Verify(request.Password, pending.PasswordHash))
                    throw new ConflictException("가입 대기 중인 이메일입니다. 입력 정보를 확인하거나 잠시 후 다시 시도해 주세요.");
                var resetCredential = NewCancellationToken(pending);
                // Resume only with the original credentials, without creating a member or session.
                if (pending.EmailVerifiedAt is not null)
                {
                    var challenge = NewChallenge(pending);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return RegistrationResponse(pending, resetCredential, challenge);
                }
                if (pending.VerificationSentAt.AddSeconds(cognitoOptions.ResendCooldownSeconds) <= Now)
                    await SendAgainAsync(pending, cancellationToken);
                else await dbContext.SaveChangesAsync(cancellationToken);
                return RegistrationResponse(pending, resetCredential);
            }
            if (await dbContext.PendingRegistrations.AnyAsync(x => x.Username == username, cancellationToken))
                throw new ConflictException("이미 사용 중이거나 가입 대기 중인 아이디입니다.");

            var hash = passwordHasher.Hash(request.Password);
            await emailVerificationProvider.SignUpAsync(email, request.Password, cancellationToken);
            var sent = Now;
            pending = new PendingRegistration
            {
                Email = email, Username = username, PasswordHash = hash,
                VerificationSentAt = sent, VerificationExpiresAt = sent.Add(VerificationLifetime),
                ExpiresAt = sent.Add(VerificationLifetime)
            };
            var resetToken = NewCancellationToken(pending);
            dbContext.PendingRegistrations.Add(pending);
            await dbContext.SaveChangesAsync(cancellationToken);
            return RegistrationResponse(pending, resetToken);
        }, cancellationToken);
    }

    public async Task<RegistrationChallenge> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        return await AuthUserTransaction.RunAsync(dbContext, email, async () =>
        {
            var pending = await dbContext.PendingRegistrations.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
            if (pending is null || pending.EmailVerifiedAt is not null)
                throw new ValidationException(InvalidVerificationCodeMessage);
            EnsureVerificationActive(pending);
            await emailVerificationProvider.ConfirmSignUpAsync(email, request.Code, cancellationToken);
            pending.EmailVerifiedAt = Now;
            pending.ExpiresAt = Now.AddHours(24);
            var challenge = NewChallenge(pending);
            await dbContext.SaveChangesAsync(cancellationToken);
            return challenge;
        }, cancellationToken);
    }

    public async Task<AuthResponse> AcceptRegistrationTermsAsync(AcceptRegistrationTermsRequest request, CancellationToken cancellationToken)
    {
        if (!request.AcceptTerms || !request.ConfirmSignature)
            throw new ValidationException("약관 동의와 전자서명 확인을 모두 선택해야 회원가입을 완료할 수 있습니다.");
        if (request.TermsVersion != CopyrightTerms.Version)
            throw new ValidationException("약관이 변경되었습니다. 페이지를 새로고침하고 최신 약관을 확인해 주세요.");
        var email = NormalizeEmail(request.Email);
        return await AuthUserTransaction.RunAsync(dbContext, email, async () =>
        {
            var pending = await dbContext.PendingRegistrations.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
            if (pending is null || pending.EmailVerifiedAt is null || pending.ExpiresAt <= Now ||
                pending.ConsentTokenExpiresAt is null || pending.ConsentTokenExpiresAt <= Now ||
                pending.ConsentTokenHash is null || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(pending.ConsentTokenHash), Encoding.UTF8.GetBytes(Hash(request.Token))))
                throw new UnauthorizedException("가입 확인 시간이 만료되었거나 올바르지 않습니다. 회원가입 정보를 다시 입력해 주세요.");
            if (!string.Equals(request.Signature.Trim(), pending.Username, StringComparison.Ordinal))
                throw new ValidationException("전자서명란에 가입한 아이디를 정확히 입력해 주세요.");
            if (dbContext.Database.IsRelational())
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({"registration-username:" + pending.Username}, 0))", cancellationToken);
            if (await dbContext.Users.AnyAsync(x => x.Email == email || x.Username == pending.Username, cancellationToken))
                throw new ConflictException("이미 사용 중인 이메일 또는 아이디입니다.");

            var user = new User
            {
                Email = pending.Email, Username = pending.Username, PasswordHash = pending.PasswordHash,
                IsEmailVerified = true, EmailVerifiedAt = pending.EmailVerifiedAt,
                CopyrightTermsAcceptedAt = Now, CopyrightTermsVersion = CopyrightTerms.Version,
                CopyrightTermsDocumentHash = CopyrightTerms.DocumentHash, CopyrightSignature = request.Signature.Trim(),
                Setting = new UserSetting()
            };
            dbContext.Users.Add(user);
            dbContext.PendingRegistrations.Remove(pending);
            await dbContext.SaveChangesAsync(cancellationToken);
            return await CreateResponseAsync(user, cancellationToken);
        }, cancellationToken);
    }

    public async Task<VerificationEmailResponse> ResendVerificationEmailAsync(ResendVerificationEmailRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        return await AuthUserTransaction.RunAsync(dbContext, email, async () =>
        {
            var pending = await dbContext.PendingRegistrations.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
            if (pending is null || pending.EmailVerifiedAt is not null)
                return new VerificationEmailResponse("인증 메일 요청이 접수되었습니다.", null, null, null, Now);
            EnsureVerificationActive(pending);
            if (pending.VerificationSentAt.AddSeconds(cognitoOptions.ResendCooldownSeconds) <= Now)
                await SendAgainAsync(pending, cancellationToken);
            return new VerificationEmailResponse("인증 메일 요청이 접수되었습니다.", pending.VerificationSentAt,
                pending.VerificationExpiresAt, pending.VerificationSentAt.AddSeconds(cognitoOptions.ResendCooldownSeconds), Now);
        }, cancellationToken);
    }

    public async Task CancelRegistrationAsync(CancelRegistrationRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        await AuthUserTransaction.RunAsync(dbContext, email, async () =>
        {
            var pending = await dbContext.PendingRegistrations.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
            if (pending is null) return true; // Repeated cancellation is harmless.
            if (await dbContext.Users.AnyAsync(x => x.Email == email, cancellationToken))
                throw new ConflictException("이미 가입이 완료된 계정은 가입 대기 취소로 삭제할 수 없습니다.");
            var tokenMatches = pending.CancellationTokenHash is not null && !string.IsNullOrEmpty(request.Token) &&
                CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(pending.CancellationTokenHash), Encoding.UTF8.GetBytes(Hash(request.Token)));
            // Original credentials also let pre-update browsers recover safely.
            if (!tokenMatches && (string.IsNullOrEmpty(request.Password) || !passwordHasher.Verify(request.Password, pending.PasswordHash)))
                throw new UnauthorizedException("가입 정보 초기화를 위해 가입 시 입력한 비밀번호를 확인해 주세요.");
            if (accountCleaner is null) throw new ConfigurationException("가입 대기 취소 서비스를 사용할 수 없습니다.");
            // The caller proved control of this registration and the member
            // absence was checked under the same email lock as verification.
            if (!await accountCleaner.DeletePendingRegistrationAsync(email, true, cancellationToken))
                throw new ServiceUnavailableException("가입 대기를 취소하지 못했습니다. 잠시 후 다시 시도해 주세요.");
            dbContext.PendingRegistrations.Remove(pending);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    private async Task SendAgainAsync(PendingRegistration pending, CancellationToken cancellationToken)
    {
        await emailVerificationProvider.ResendConfirmationCodeAsync(pending.Email, cancellationToken);
        pending.VerificationSentAt = Now;
        pending.VerificationExpiresAt = Now.Add(VerificationLifetime);
        pending.ExpiresAt = pending.VerificationExpiresAt;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private RegistrationChallenge NewChallenge(PendingRegistration pending)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        pending.ConsentTokenHash = Hash(token);
        pending.ConsentTokenExpiresAt = Now.AddMinutes(15);
        return new RegistrationChallenge(pending.Email, pending.Username, token,
            pending.ConsentTokenExpiresAt.Value, CopyrightTerms.Version);
    }

    private static string NewCancellationToken(PendingRegistration pending)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        pending.CancellationTokenHash = Hash(token);
        return token;
    }

    private RegisterResponse RegistrationResponse(PendingRegistration pending, string cancellationToken, RegistrationChallenge? challenge = null) =>
        new(pending.Email, pending.Username, pending.EmailVerifiedAt is null, pending.VerificationSentAt,
            pending.VerificationExpiresAt, pending.VerificationSentAt.AddSeconds(cognitoOptions.ResendCooldownSeconds), Now, cancellationToken, challenge);
    public async Task<FindIdResponse> FindIdAsync(
        FindIdRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var username = await dbContext.Users
            .AsNoTracking()
            .Where(x => x.Email == email && x.IsEmailVerified && x.DeletionRequestedAt == null)
            .Select(x => x.Username)
            .SingleOrDefaultAsync(cancellationToken);

        return new FindIdResponse(username);
    }

    public async Task StartPasswordResetAsync(
        PasswordResetStartRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var userExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(x => x.Email == email && x.IsEmailVerified && x.DeletionRequestedAt == null, cancellationToken);

        if (!userExists)
        {
            return;
        }

        await passwordRecoveryProvider.StartPasswordResetAsync(email, cancellationToken);
    }

    public async Task ConfirmPasswordResetAsync(
        PasswordResetConfirmRequest request,
        CancellationToken cancellationToken)
    {
        if (!PasswordPolicy.IsSatisfiedBy(request.NewPassword))
        {
            throw new ValidationException(PasswordPolicy.ErrorMessage);
        }

        var email = NormalizeEmail(request.Email);
        var user = await dbContext.Users.SingleOrDefaultAsync(
            x => x.Email == email && x.IsEmailVerified && x.DeletionRequestedAt == null,
            cancellationToken);

        if (user is null)
        {
            throw new ValidationException(InvalidPasswordResetMessage);
        }

        await passwordRecoveryProvider.ConfirmPasswordResetAsync(
            email,
            request.Code,
            request.NewPassword,
            cancellationToken);

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        foreach (var session in await dbContext.AuthSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null).ToListAsync(cancellationToken))
            session.RevokedAt = Now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var identifier = request.Email.Trim();
        User? user;
        if (identifier.Contains('@'))
        {
            var email = NormalizeEmail(identifier);
            user = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        }
        else
        {
            user = await dbContext.Users.SingleOrDefaultAsync(x => x.Username == identifier, cancellationToken);
        }

        if (user is null)
        {
            var pendingEmail = NormalizeEmail(identifier);
            var pending = await dbContext.PendingRegistrations.AsNoTracking().SingleOrDefaultAsync(
                x => x.Email == pendingEmail || x.Username == identifier, cancellationToken);
            if (pending is not null && pending.ExpiresAt > Now && passwordHasher.Verify(request.Password, pending.PasswordHash))
                throw new ForbiddenException("이메일 인증과 약관 동의가 필요합니다. 회원가입 화면에 같은 정보를 입력해 이어서 진행해 주세요.");
        }
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("이메일/사용자 아이디 또는 비밀번호가 올바르지 않습니다.");
        }

        if (!user.IsEmailVerified)
        {
            throw new ForbiddenException("이메일 인증이 필요합니다.");
        }

        return await CreateResponseAsync(user, cancellationToken);
    }

    public async Task<UserResponse> GetMeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new NotFoundException("사용자를 찾을 수 없습니다.");
        if (user.DeletionRequestedAt != null) throw new UnauthorizedException("삭제 요청된 계정입니다.");
        return MapUser(user);
    }

    private async Task<AuthResponse> CreateResponseAsync(User user, CancellationToken cancellationToken)
    {
        if (user.DeletionRequestedAt != null) throw new UnauthorizedException("삭제 요청된 계정입니다.");
        var refreshToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var refreshExpiry = Now.AddDays(90);
        var session = new AuthSession
        {
            UserId = user.Id,
            TokenHash = Hash(refreshToken),
            PasswordFingerprint = Hash(user.PasswordHash),
            ExpiresAt = refreshExpiry
        };
        dbContext.AuthSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        var (token, expiresAt) = jwtTokenService.Create(user, session.Id);
        return new AuthResponse(token, expiresAt, MapUser(user), refreshToken, refreshExpiry);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshSessionRequest request, CancellationToken cancellationToken)
    {
        var hash = Hash(request.RefreshToken);
        var session = await dbContext.AuthSessions.Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        var now = Now;
        if (session is null || session.RevokedAt != null || session.ExpiresAt <= now ||
            session.CreatedAt.AddDays(365) <= now || !session.User.IsEmailVerified ||
            session.PasswordFingerprint != Hash(session.User.PasswordHash))
            throw new UnauthorizedException("자동 로그인 정보가 만료되었습니다. 다시 로그인해 주세요.");

        // A stable, random credential survives lost refresh responses. It is stored
        // only as a hash, revoked on logout, and expires after 90 idle days/1 year.
        session.ExpiresAt = new[] { now.AddDays(90), session.CreatedAt.AddDays(365) }.Min();
        session.UpdatedAt = now;
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        { throw new UnauthorizedException("로그아웃된 로그인 정보입니다."); }
        var (token, expiresAt) = jwtTokenService.Create(session.User, session.Id);
        return new AuthResponse(token, expiresAt, MapUser(session.User), request.RefreshToken, session.ExpiresAt);
    }

    public async Task RevokeSessionAsync(RefreshSessionRequest request, CancellationToken cancellationToken)
    {
        var hash = Hash(request.RefreshToken);
        var session = await dbContext.AuthSessions.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (session is null || session.RevokedAt != null) return;
        session.RevokedAt = Now;
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { /* Another logout already revoked it. */ }
    }

    public async Task DeleteExpiredRegistrationsAndSessionsAsync(CancellationToken cancellationToken)
    {
        var now = Now;
        var emails = await dbContext.PendingRegistrations.AsNoTracking().Where(x => x.ExpiresAt <= now)
            .Select(x => x.Email).ToListAsync(cancellationToken);
        foreach (var email in emails)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { await DeleteExpiredPendingAsync(email, cancellationToken); }
            catch (Exception error) when (error is not OperationCanceledException)
            { logger?.LogError(error, "Expired pending registration cleanup failed; it will be retried."); }
        }
        var expiredSessions = await dbContext.AuthSessions
            .Where(x => x.ExpiresAt <= now || x.RevokedAt != null || x.CreatedAt <= now.AddDays(-365))
            .ToListAsync(cancellationToken);
        dbContext.AuthSessions.RemoveRange(expiredSessions);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<bool> DeleteExpiredPendingAsync(string email, CancellationToken cancellationToken) =>
        AuthUserTransaction.RunAsync(dbContext, email, async () =>
        {
            var pending = await dbContext.PendingRegistrations.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
            if (pending is null || pending.ExpiresAt > Now) return false;
            // Never delete a member or its external identity.
            if (!await dbContext.Users.AnyAsync(x => x.Email == email, cancellationToken))
            {
                if (accountCleaner is null) throw new InvalidOperationException("Pending registration cleanup provider is required.");
                if (!await accountCleaner.DeletePendingRegistrationAsync(email, pending.EmailVerifiedAt is not null, cancellationToken)) return false;
            }
            dbContext.PendingRegistrations.Remove(pending);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);

    private void EnsureVerificationActive(PendingRegistration pending)
    {
        if (pending.VerificationExpiresAt <= Now)
            throw new ValidationException("이메일 인증 시간이 만료되었습니다. 회원가입 정보를 다시 입력해 주세요.");
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static UserResponse MapUser(User user) =>
        new(user.Id, user.Email, user.Username, user.IsEmailVerified, user.CreatedAt);

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

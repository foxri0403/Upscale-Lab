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

    public async Task<RegisterResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var username = request.Username.Trim();

        if (!PasswordPolicy.IsSatisfiedBy(request.Password))
        {
            throw new ValidationException(PasswordPolicy.ErrorMessage);
        }

        var expiredCandidates = await dbContext.Users.AsNoTracking()
            .Where(x => !x.IsEmailVerified && (x.Email == email || x.Username == username))
            .Select(x => x.Email).ToListAsync(cancellationToken);
        foreach (var candidate in expiredCandidates)
            await DeleteExpiredEmailAsync(candidate, cancellationToken);

        if (await dbContext.Users.AnyAsync(x => x.Email == email, cancellationToken))
        {
            throw new ConflictException("이미 사용 중인 이메일입니다.");
        }

        if (await dbContext.Users.AnyAsync(x => x.Username == username, cancellationToken))
        {
            throw new ConflictException("이미 사용 중인 사용자 이름입니다.");
        }

        var now = Now;
        var user = new User
        {
            Email = email,
            Username = username,
            PasswordHash = passwordHasher.Hash(request.Password),
            EmailVerificationSentAt = now,
            EmailVerificationExpiresAt = now.Add(VerificationLifetime),
            Setting = new UserSetting()
        };

        await emailVerificationProvider.SignUpAsync(
            email,
            request.Password,
            cancellationToken);

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterResponse(
            MapUser(user),
            RequiresEmailVerification: true,
            user.EmailVerificationExpiresAt.Value);
    }

    public async Task<AuthResponse> VerifyEmailAsync(
        VerifyEmailRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        return await AuthUserTransaction.RunAsync(dbContext, email, async () =>
        {
            var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);

            if (user is null || user.IsEmailVerified)
            {
                throw new ValidationException(InvalidVerificationCodeMessage);
            }

            EnsureVerificationActive(user);

            await emailVerificationProvider.ConfirmSignUpAsync(email, request.Code, cancellationToken);

            user.IsEmailVerified = true;
            user.EmailVerifiedAt = Now;
            user.EmailVerificationSentAt = null;
            user.EmailVerificationExpiresAt = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            return await CreateResponseAsync(user, cancellationToken);
        }, cancellationToken);
    }

    public async Task ResendVerificationEmailAsync(
        ResendVerificationEmailRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        await AuthUserTransaction.RunAsync(dbContext, email, async () =>
        {
            var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
            var now = Now;

            if (user is null || user.IsEmailVerified)
            {
                return false;
            }

            EnsureVerificationActive(user);

            if (user.EmailVerificationSentAt is not null &&
                user.EmailVerificationSentAt.Value.AddSeconds(cognitoOptions.ResendCooldownSeconds) > now)
            {
                return false;
            }

            await emailVerificationProvider.ResendConfirmationCodeAsync(email, cancellationToken);
            user.EmailVerificationSentAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

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

    public async Task DeleteExpiredAccountsAsync(CancellationToken cancellationToken)
    {
        var now = Now;
        var legacyCutoff = now.Subtract(VerificationLifetime);
        var emails = await dbContext.Users.AsNoTracking().Where(x => !x.IsEmailVerified &&
            (x.EmailVerificationExpiresAt != null ? x.EmailVerificationExpiresAt <= now : x.CreatedAt <= legacyCutoff))
            .Select(x => x.Email).ToListAsync(cancellationToken);
        foreach (var email in emails)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { await DeleteExpiredEmailAsync(email, cancellationToken); }
            catch (Exception error) when (error is not OperationCanceledException)
            { logger?.LogError(error, "Expired account cleanup failed; it will be retried."); }
        }
        var expiredSessions = await dbContext.AuthSessions
            .Where(x => x.ExpiresAt <= now || x.RevokedAt != null || x.CreatedAt <= now.AddDays(-365))
            .ToListAsync(cancellationToken);
        dbContext.AuthSessions.RemoveRange(expiredSessions);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<bool> DeleteExpiredEmailAsync(string email, CancellationToken cancellationToken) =>
        AuthUserTransaction.RunAsync(dbContext, email, async () =>
        {
            var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
            if (user is null || user.IsEmailVerified || VerificationExpiry(user) > Now) return false;
            if (accountCleaner is null) throw new InvalidOperationException("Account cleanup provider is required.");
            if (!await accountCleaner.DeleteUnconfirmedAsync(email, cancellationToken)) return false;
            dbContext.Users.Remove(user);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);

    private void EnsureVerificationActive(User user)
    {
        if (VerificationExpiry(user) <= Now)
            throw new ValidationException("이메일 인증 시간이 만료되었습니다. 15분 이내에 인증을 완료해야 합니다. 다시 가입해 주세요.");
    }

    private static DateTime VerificationExpiry(User user) =>
        user.EmailVerificationExpiresAt ?? user.CreatedAt.Add(VerificationLifetime);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static UserResponse MapUser(User user) =>
        new(user.Id, user.Email, user.Username, user.IsEmailVerified, user.CreatedAt);

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

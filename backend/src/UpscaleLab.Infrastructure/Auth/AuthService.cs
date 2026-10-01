using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Common;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Email;

namespace UpscaleLab.Infrastructure.Auth;

public sealed class AuthService(
    ApplicationDbContext dbContext,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    IEmailSender emailSender,
    IEmailVerificationCodeProtector verificationCodeProtector,
    EmailVerificationOptions emailOptions) : IAuthService
{
    private const string InvalidVerificationCodeMessage =
        "인증 코드가 올바르지 않거나 만료되었습니다. 새 코드를 요청해 주세요.";

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

        if (await dbContext.Users.AnyAsync(x => x.Email == email, cancellationToken))
        {
            throw new ConflictException("이미 사용 중인 이메일입니다.");
        }

        if (await dbContext.Users.AnyAsync(x => x.Username == username, cancellationToken))
        {
            throw new ConflictException("이미 사용 중인 사용자 이름입니다.");
        }

        var user = new User
        {
            Email = email,
            Username = username,
            PasswordHash = passwordHasher.Hash(request.Password),
            Setting = new UserSetting()
        };
        var verificationCode = SetNewVerificationCode(user, DateTime.UtcNow);

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        await emailSender.SendVerificationCodeAsync(
            user.Email,
            user.Username,
            verificationCode,
            emailOptions.CodeExpirationMinutes,
            cancellationToken);

        return new RegisterResponse(
            MapUser(user),
            RequiresEmailVerification: true,
            user.EmailVerificationCodeExpiresAt!.Value);
    }

    public async Task<AuthResponse> VerifyEmailAsync(
        VerifyEmailRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        var now = DateTime.UtcNow;

        if (user is null ||
            user.IsEmailVerified ||
            user.EmailVerificationCodeHash is null ||
            user.EmailVerificationCodeExpiresAt is null ||
            user.EmailVerificationCodeExpiresAt <= now ||
            user.EmailVerificationFailedAttempts >= emailOptions.MaxFailedAttempts)
        {
            throw new ValidationException(InvalidVerificationCodeMessage);
        }

        if (!verificationCodeProtector.Verify(email, request.Code, user.EmailVerificationCodeHash))
        {
            user.EmailVerificationFailedAttempts++;
            if (user.EmailVerificationFailedAttempts >= emailOptions.MaxFailedAttempts)
            {
                ClearVerificationCode(user);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            throw new ValidationException(InvalidVerificationCodeMessage);
        }

        user.IsEmailVerified = true;
        user.EmailVerifiedAt = now;
        ClearVerificationCode(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateResponse(user);
    }

    public async Task ResendVerificationEmailAsync(
        ResendVerificationEmailRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        var now = DateTime.UtcNow;

        if (user is null || user.IsEmailVerified)
        {
            return;
        }

        if (user.EmailVerificationCodeSentAt is not null &&
            user.EmailVerificationCodeSentAt.Value.AddSeconds(emailOptions.ResendCooldownSeconds) > now)
        {
            return;
        }

        var verificationCode = SetNewVerificationCode(user, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        await emailSender.SendVerificationCodeAsync(
            user.Email,
            user.Username,
            verificationCode,
            emailOptions.CodeExpirationMinutes,
            cancellationToken);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);

        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("이메일 또는 비밀번호가 올바르지 않습니다.");
        }

        if (!user.IsEmailVerified)
        {
            throw new ForbiddenException("이메일 인증이 필요합니다.");
        }

        return CreateResponse(user);
    }

    public async Task<UserResponse> GetMeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new NotFoundException("사용자를 찾을 수 없습니다.");

        return MapUser(user);
    }

    private AuthResponse CreateResponse(User user)
    {
        var (token, expiresAt) = jwtTokenService.Create(user);
        return new AuthResponse(token, expiresAt, MapUser(user));
    }

    private string SetNewVerificationCode(User user, DateTime now)
    {
        var code = verificationCodeProtector.Generate();
        user.EmailVerificationCodeHash = verificationCodeProtector.Hash(user.Email, code);
        user.EmailVerificationCodeExpiresAt = now.AddMinutes(emailOptions.CodeExpirationMinutes);
        user.EmailVerificationCodeSentAt = now;
        user.EmailVerificationFailedAttempts = 0;
        return code;
    }

    private static void ClearVerificationCode(User user)
    {
        user.EmailVerificationCodeHash = null;
        user.EmailVerificationCodeExpiresAt = null;
        user.EmailVerificationCodeSentAt = null;
        user.EmailVerificationFailedAttempts = 0;
    }

    private static UserResponse MapUser(User user) =>
        new(user.Id, user.Email, user.Username, user.IsEmailVerified, user.CreatedAt);

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

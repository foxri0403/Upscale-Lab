using Microsoft.EntityFrameworkCore;
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
    CognitoOptions cognitoOptions) : IAuthService
{
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
            EmailVerificationSentAt = DateTime.UtcNow,
            Setting = new UserSetting()
        };

        var verificationCodeExpiresAt = await emailVerificationProvider.SignUpAsync(
            email,
            request.Password,
            cancellationToken);

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterResponse(
            MapUser(user),
            RequiresEmailVerification: true,
            verificationCodeExpiresAt);
    }

    public async Task<AuthResponse> VerifyEmailAsync(
        VerifyEmailRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);

        if (user is null || user.IsEmailVerified)
        {
            throw new ValidationException(InvalidVerificationCodeMessage);
        }

        await emailVerificationProvider.ConfirmSignUpAsync(email, request.Code, cancellationToken);

        user.IsEmailVerified = true;
        user.EmailVerifiedAt = DateTime.UtcNow;
        user.EmailVerificationSentAt = null;
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

        if (user.EmailVerificationSentAt is not null &&
            user.EmailVerificationSentAt.Value.AddSeconds(cognitoOptions.ResendCooldownSeconds) > now)
        {
            return;
        }

        await emailVerificationProvider.ResendConfirmationCodeAsync(email, cancellationToken);
        user.EmailVerificationSentAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<FindIdResponse> FindIdAsync(
        FindIdRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var username = await dbContext.Users
            .AsNoTracking()
            .Where(x => x.Email == email && x.IsEmailVerified)
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
            .AnyAsync(x => x.Email == email && x.IsEmailVerified, cancellationToken);

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
            x => x.Email == email && x.IsEmailVerified,
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

    private static UserResponse MapUser(User user) =>
        new(user.Id, user.Email, user.Username, user.IsEmailVerified, user.CreatedAt);

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

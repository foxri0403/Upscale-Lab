using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Common;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Auth;

public sealed class AuthService(
    ApplicationDbContext dbContext,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var username = request.Username.Trim();

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

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);

        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("이메일 또는 비밀번호가 올바르지 않습니다.");
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

    private static UserResponse MapUser(User user) => new(user.Id, user.Email, user.Username, user.CreatedAt);

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

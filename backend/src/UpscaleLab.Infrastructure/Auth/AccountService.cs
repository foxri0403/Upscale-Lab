using ImageMagick;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Auth;

public sealed class AccountService(ApplicationDbContext db, IPasswordHasher passwords, IStorageService storage,
    IAccountIdentityRemover identity, TimeProvider? clock = null) : IAccountService
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    public Task<AccountProfile> GetAsync(Guid userId, CancellationToken ct) => ReadProfileAsync(userId, ct);

    public async Task<AccountProfile> UpdateAsync(Guid userId, UpdateAccountRequest request, CancellationToken ct)
    {
        var name = request.Username.Trim();
        if (name.Length is < 2 or > 50 || name.Contains('@') || name.Any(char.IsControl))
            throw new ValidationException("아이디는 @를 제외한 2~50자로 입력해 주세요.");
        var email = (await RequireUserAsync(userId, ct)).Email;
        return await AuthUserTransaction.RunAsync(db, email, async () =>
        {
            var user = await RequireUserAsync(userId, ct);
            VerifyPassword(user, request.CurrentPassword);
            if (await db.Users.AnyAsync(x => x.Id != userId && x.Username == name, ct))
                throw new ConflictException("이미 사용 중인 아이디입니다.");
            user.Username = name;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { throw new ConflictException("아이디를 저장하지 못했습니다. 다른 아이디로 다시 시도해 주세요."); }
            return Profile(user);
        }, ct);
    }

    public async Task<AccountProfile> UploadPhotoAsync(Guid userId, Stream source, CancellationToken ct)
    {
        var email = (await RequireUserAsync(userId, ct)).Email;
        var bytes = await ReadPhotoAsync(source, ct);
        return await AuthUserTransaction.RunAsync(db, email, async () =>
        {
            var user = await RequireUserAsync(userId, ct);
            var key = $"users/{userId:N}/profile/{Guid.NewGuid():N}.png";
            await using var image = new MemoryStream(bytes);
            await storage.UploadAsync(image, key, "image/png", ct);
            try
            {
                QueueObject(userId, user.ProfileImageObjectKey);
                user.ProfileImageObjectKey = key;
                await db.SaveChangesAsync(ct);
                return Profile(user);
            }
            catch
            {
                try { await storage.DeleteAsync(key, CancellationToken.None); } catch { /* Original exception remains actionable. */ }
                throw;
            }
        }, ct);
    }

    public async Task<AccountProfile> RemovePhotoAsync(Guid userId, CancellationToken ct)
    {
        var email = (await RequireUserAsync(userId, ct)).Email;
        return await AuthUserTransaction.RunAsync(db, email, async () =>
        {
            var user = await RequireUserAsync(userId, ct);
            QueueObject(userId, user.ProfileImageObjectKey);
            user.ProfileImageObjectKey = null;
            await db.SaveChangesAsync(ct);
            return Profile(user);
        }, ct);
    }

    public async Task RequestDeletionAsync(Guid userId, DeleteAccountRequest request, CancellationToken ct)
    {
        if (request.Confirmation != "계정 삭제") throw new ValidationException("‘계정 삭제’를 정확히 입력해 주세요.");
        var email = (await RequireUserAsync(userId, ct)).Email;
        await AuthUserTransaction.RunAsync(db, email, async () =>
        {
            var user = await RequireUserAsync(userId, ct);
            VerifyPassword(user, request.CurrentPassword);
            if (await db.ProcessingJobs.AnyAsync(x => x.Project.UserId == userId &&
                (x.Status == ProcessingJobStatus.Queued || x.Status == ProcessingJobStatus.Processing), ct))
                throw new ConflictException("진행 중인 이미지 작업이 끝난 뒤 계정을 삭제해 주세요.");
            // Imported worker tables are optional in older installations.
            if (db.Database.IsNpgsql() && await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('public.inpainting_jobs') IS NOT NULL AS \"Value\"").SingleAsync(ct) &&
                await db.Database.SqlQuery<int>($"SELECT COUNT(*)::integer AS \"Value\" FROM inpainting_jobs WHERE \"UserId\" = {userId} AND \"Status\" IN ('Queued', 'Processing')").SingleAsync(ct) > 0)
                throw new ConflictException("진행 중인 이미지 작업이 끝난 뒤 계정을 삭제해 주세요.");
            var subject = await identity.GetSubjectAsync(user.Email, ct);
            var now = Now;
            user.DeletionRequestedAt = now;
            user.PasswordHash = passwords.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            foreach (var session in await db.AuthSessions.Where(x => x.UserId == userId && x.RevokedAt == null).ToListAsync(ct))
                session.RevokedAt = now;
            db.AccountCleanupTasks.Add(new AccountCleanupTask { UserId = userId, Email = email, IdentitySubject = subject,
                DeleteAccount = true, NextAttemptAt = now.AddSeconds(30) });
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    private async Task<User> RequireUserAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("계정을 찾을 수 없습니다.");
        if (!user.IsEmailVerified || user.DeletionRequestedAt != null) throw new UnauthorizedException("사용할 수 없는 계정입니다.");
        return user;
    }
    private async Task<AccountProfile> ReadProfileAsync(Guid id, CancellationToken ct) => Profile(await RequireUserAsync(id, ct));
    private AccountProfile Profile(User user) => new(user.Id, user.Email, user.Username,
        user.ProfileImageObjectKey is { } key ? storage.CreateDownloadUrl(key, TimeSpan.FromMinutes(15)) : null, user.CreatedAt);
    private void VerifyPassword(User user, string password)
    {
        if (!passwords.Verify(password, user.PasswordHash)) throw new ValidationException("현재 비밀번호가 올바르지 않습니다.");
    }
    private void QueueObject(Guid userId, string? key)
    {
        if (!string.IsNullOrWhiteSpace(key)) db.AccountCleanupTasks.Add(new AccountCleanupTask { UserId = userId, ObjectKey = key, NextAttemptAt = Now });
    }
    private static async Task<byte[]> ReadPhotoAsync(Stream source, CancellationToken ct)
    {
        await using var content = new MemoryStream();
        var buffer = new byte[81920]; int count;
        while ((count = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (content.Length + count > 5 * 1024 * 1024) throw new ValidationException("프로필 사진은 5MB 이하로 선택해 주세요.");
            await content.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        try
        {
            content.Position = 0; var info = new MagickImageInfo(content);
            if (info.Format is not (MagickFormat.Jpeg or MagickFormat.Png or MagickFormat.WebP) ||
                info.Width < 1 || info.Height < 1 || (long)info.Width * info.Height > 16_000_000)
                throw new ValidationException("1600만 화소 이하의 JPG, PNG, WebP 사진을 선택해 주세요.");
            content.Position = 0;
            using var image = new MagickImage(content, new MagickReadSettings { FrameIndex = 0, FrameCount = 1 });
            image.AutoOrient(); image.ColorSpace = ColorSpace.sRGB;
            if (image.Width > 512 || image.Height > 512) image.Resize(512, 512);
            image.Strip(); return image.ToByteArray(MagickFormat.Png);
        }
        catch (MagickException) { throw new ValidationException("프로필 사진을 읽을 수 없습니다. JPG, PNG, WebP 파일을 확인해 주세요."); }
    }
}

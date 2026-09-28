using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Settings;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Settings;

public sealed class UserSettingService(ApplicationDbContext dbContext) : IUserSettingService
{
    public async Task<UserSettingResponse> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var setting = await GetOrCreateAsync(userId, cancellationToken);
        return Map(setting);
    }

    public async Task<UserSettingResponse> UpdateAsync(
        Guid userId,
        UpdateUserSettingRequest request,
        CancellationToken cancellationToken)
    {
        var setting = await GetOrCreateAsync(userId, cancellationToken);
        setting.SyncEnabled = request.SyncEnabled;
        setting.DynamicOrientation = request.DynamicOrientation;
        setting.AutoUpscale = request.AutoUpscale;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(setting);
    }

    private async Task<UserSetting> GetOrCreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var setting = await dbContext.UserSettings.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (setting is not null)
        {
            return setting;
        }

        setting = new UserSetting { UserId = userId };
        dbContext.UserSettings.Add(setting);
        await dbContext.SaveChangesAsync(cancellationToken);
        return setting;
    }

    private static UserSettingResponse Map(UserSetting setting) => new(
        setting.Id,
        setting.SyncEnabled,
        setting.DynamicOrientation,
        setting.AutoUpscale,
        setting.UpdatedAt);
}

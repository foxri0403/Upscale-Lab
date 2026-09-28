namespace UpscaleLab.Application.Settings;

public interface IUserSettingService
{
    Task<UserSettingResponse> GetAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserSettingResponse> UpdateAsync(Guid userId, UpdateUserSettingRequest request, CancellationToken cancellationToken);
}

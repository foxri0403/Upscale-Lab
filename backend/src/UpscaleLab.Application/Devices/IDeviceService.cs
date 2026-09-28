namespace UpscaleLab.Application.Devices;

public interface IDeviceService
{
    Task<IReadOnlyList<DeviceResponse>> GetAllAsync(Guid userId, CancellationToken cancellationToken);
    Task<DeviceResponse> CreateAsync(Guid userId, DeviceRequest request, CancellationToken cancellationToken);
    Task<DeviceResponse> UpdateAsync(Guid userId, Guid deviceId, DeviceRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken);
}

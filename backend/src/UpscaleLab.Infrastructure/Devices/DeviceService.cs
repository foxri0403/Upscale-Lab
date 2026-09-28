using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Devices;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Devices;

public sealed class DeviceService(ApplicationDbContext dbContext) : IDeviceService
{
    public async Task<IReadOnlyList<DeviceResponse>> GetAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await dbContext.Devices
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new DeviceResponse(
                x.Id,
                x.Platform,
                x.DeviceName,
                x.ScreenWidth,
                x.ScreenHeight,
                x.AspectRatio,
                x.Orientation,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<DeviceResponse> CreateAsync(Guid userId, DeviceRequest request, CancellationToken cancellationToken)
    {
        var device = new Device { UserId = userId };
        Apply(device, request);
        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(device);
    }

    public async Task<DeviceResponse> UpdateAsync(
        Guid userId,
        Guid deviceId,
        DeviceRequest request,
        CancellationToken cancellationToken)
    {
        var device = await FindOwnedAsync(userId, deviceId, cancellationToken);
        Apply(device, request);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(device);
    }

    public async Task DeleteAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken)
    {
        var device = await FindOwnedAsync(userId, deviceId, cancellationToken);
        dbContext.Devices.Remove(device);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Device> FindOwnedAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken)
    {
        return await dbContext.Devices.SingleOrDefaultAsync(
            x => x.Id == deviceId && x.UserId == userId,
            cancellationToken) ?? throw new NotFoundException("디바이스를 찾을 수 없습니다.");
    }

    private static void Apply(Device device, DeviceRequest request)
    {
        device.Platform = request.Platform;
        device.DeviceName = request.DeviceName.Trim();
        device.ScreenWidth = request.ScreenWidth;
        device.ScreenHeight = request.ScreenHeight;
        device.AspectRatio = request.AspectRatio.Trim();
        device.Orientation = request.Orientation;
    }

    private static DeviceResponse Map(Device device) => new(
        device.Id,
        device.Platform,
        device.DeviceName,
        device.ScreenWidth,
        device.ScreenHeight,
        device.AspectRatio,
        device.Orientation,
        device.CreatedAt,
        device.UpdatedAt);
}

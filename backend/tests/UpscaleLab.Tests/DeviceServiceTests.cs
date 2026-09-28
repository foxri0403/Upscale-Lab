using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Devices;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Devices;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class DeviceServiceTests
{
    [Fact]
    public async Task Update_DoesNotAllowAnotherUsersDevice()
    {
        await using var dbContext = CreateDbContext();
        var service = new DeviceService(dbContext);
        var ownerId = Guid.NewGuid();
        var request = new DeviceRequest(
            DevicePlatform.Windows,
            "Desktop",
            2560,
            1440,
            "16:9",
            ScreenOrientation.Landscape);
        var device = await service.CreateAsync(ownerId, request, CancellationToken.None);

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(
            Guid.NewGuid(),
            device.Id,
            request,
            CancellationToken.None));
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyCurrentUsersDevices()
    {
        await using var dbContext = CreateDbContext();
        var service = new DeviceService(dbContext);
        var userId = Guid.NewGuid();
        var request = new DeviceRequest(
            DevicePlatform.Android,
            "Phone",
            1080,
            2400,
            "20:9",
            ScreenOrientation.Portrait);

        await service.CreateAsync(userId, request, CancellationToken.None);
        await service.CreateAsync(Guid.NewGuid(), request, CancellationToken.None);

        var result = await service.GetAllAsync(userId, CancellationToken.None);
        Assert.Single(result);
        Assert.Equal("Phone", result[0].DeviceName);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}

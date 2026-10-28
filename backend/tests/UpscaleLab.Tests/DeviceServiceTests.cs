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
    public async Task GetAll_UsesRegistrationTimeRatherThanHeartbeatOrName()
    {
        await using var db = CreateDbContext(); var user = Guid.NewGuid();
        var first = WorkflowData.Device(user);
        var second = WorkflowData.Device(user); second.DeviceName = "AAA";
        db.Devices.Add(first); await db.SaveChangesAsync();
        await Task.Delay(20);
        db.Devices.Add(second); await db.SaveChangesAsync();
        first.DeviceName = "Recently updated"; await db.SaveChangesAsync();
        var result = await new DeviceService(db).GetAllAsync(user, default);
        Assert.Equal(new[] { first.Id, second.Id }, result.Select(x => x.Id));
    }
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

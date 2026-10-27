using UpscaleLab.Api.Hubs;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class DeviceChangeFeedTests
{
    [Fact]
    public async Task HeartbeatAndReportWakeTheBrowserWithoutCreatingAnAgentFeedbackLoop()
    {
        using var feed = new DeviceChangeFeed(); var user = Guid.NewGuid(); var device = Guid.NewGuid();
        using var cancel = new CancellationTokenSource();
        var agentCursor = await feed.WaitAsync(user, device, null, cancel.Token);
        var agent = feed.WaitAsync(user, device, agentCursor, cancel.Token);
        var browserCursor = await feed.WaitAsync(user, null, null, cancel.Token);
        feed.Publish(user, device, notifyAgent: false);
        Assert.NotEqual(browserCursor, await feed.WaitAsync(user, null, browserCursor, cancel.Token));
        Assert.False(agent.IsCompleted); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => agent);
    }
    [Fact]
    public async Task AChangeWakesBrowserAndItsDeviceWithoutWakingAnotherUserOrDevice()
    {
        using var feed = new DeviceChangeFeed();
        var user = Guid.NewGuid(); var device = Guid.NewGuid(); var other = Guid.NewGuid();
        using var cancel = new CancellationTokenSource();
        var browserCursor = await feed.WaitAsync(user, null, null, cancel.Token);
        var agentCursor = await feed.WaitAsync(user, device, null, cancel.Token);
        var otherCursor = await feed.WaitAsync(user, other, null, cancel.Token);
        var foreignCursor = await feed.WaitAsync(other, null, null, cancel.Token);
        var browser = feed.WaitAsync(user, null, browserCursor, cancel.Token);
        var agent = feed.WaitAsync(user, device, agentCursor, cancel.Token);
        var anotherDevice = feed.WaitAsync(user, other, otherCursor, cancel.Token);
        var foreign = feed.WaitAsync(other, null, foreignCursor, cancel.Token);
        feed.Publish(user, device);
        Assert.NotEqual(browserCursor, await browser.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.NotEqual(agentCursor, await agent.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.False(anotherDevice.IsCompleted); Assert.False(foreign.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => anotherDevice);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => foreign);
    }
    [Fact]
    public async Task ChangeBetweenReadAndSubscribeIsNotLostAndBroadcastReachesAllTabs()
    {
        using var feed = new DeviceChangeFeed(); var user = Guid.NewGuid(); var device = Guid.NewGuid();
        var before = await feed.WaitAsync(user, null, null, CancellationToken.None);
        feed.Publish(user, device);
        var after = await feed.WaitAsync(user, null, before, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
        var first = feed.WaitAsync(user, null, after, CancellationToken.None);
        var second = feed.WaitAsync(user, null, after, CancellationToken.None);
        feed.Publish(user, device);
        Assert.Equal(await first, await second); Assert.NotEqual(before, after);
    }
}

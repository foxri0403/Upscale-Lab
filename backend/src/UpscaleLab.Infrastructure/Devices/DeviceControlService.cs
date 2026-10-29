using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Devices;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Devices;

public sealed class DeviceControlService(ApplicationDbContext db, TimeProvider clock, IDeviceEvents? events = null)
    : IDeviceControlService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static bool Active(DeviceCommand command) => command.Status is DeviceCommandStatus.Pending
        or DeviceCommandStatus.Downloading or DeviceCommandStatus.NeedsUserAction;
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<AgentTokenResponse> RotateTokenAsync(Guid userId, Guid deviceId, CancellationToken ct)
    {
        var device = await Owned(userId, deviceId, ct);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        device.AgentTokenHash = Hash(token);
        device.InventoryRevision = 0;
        device.LastSeenAt = null;
        device.SupportsWallpaperRemoval = false;
        await Save(userId, deviceId, ct, notifyAgent: true);
        return new(deviceId, token);
    }

    public async Task RevokeTokenAsync(Guid userId, Guid deviceId, CancellationToken ct)
    {
        var device = await Owned(userId, deviceId, ct);
        device.AgentTokenHash = null;
        device.SupportsWallpaperRemoval = false;
        device.LastSeenAt = null;
        foreach (var command in await db.DeviceCommands.Where(x => x.DeviceId == deviceId).ToListAsync(ct))
            if (Active(command)) command.Status = DeviceCommandStatus.Superseded;
        await Save(userId, deviceId, ct, notifyAgent: true);
    }

    public async Task<AgentIdentity?> AuthenticateAsync(string token, CancellationToken ct)
    {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit)) return null;
        var hash = Hash(token);
        return await db.Devices.AsNoTracking().Where(x => x.AgentTokenHash == hash)
            .Select(x => new AgentIdentity(x.Id, x.UserId)).SingleOrDefaultAsync(ct);
    }

    public async Task<DeviceState> GetStateAsync(Guid userId, Guid deviceId, CancellationToken ct)
    {
        var device = await Owned(userId, deviceId, ct);
        var assignments = await db.WallpaperAssignments.AsNoTracking()
            .Where(x => x.DeviceDisplay.DeviceId == deviceId).ToListAsync(ct);
        var pendingRemoval = await db.DeviceCommands.AsNoTracking().Where(x => x.DeviceId == deviceId && x.Action == DeviceCommandAction.Clear &&
            (x.Status == DeviceCommandStatus.Pending || x.Status == DeviceCommandStatus.Downloading || x.Status == DeviceCommandStatus.NeedsUserAction) && x.ExpiresAt > Now)
            .Select(x => new { x.DeviceDisplayId, x.Target, x.Sequence }).ToListAsync(ct);
        return new(device.Id, device.Platform, device.DeviceName, device.OsVersion, device.AgentVersion,
            device.AgentTokenHash != null, device.LastSeenAt,
            device.LastSeenAt.HasValue && device.LastSeenAt.Value > Now.AddMinutes(-2),
            device.InventoryRevision, device.SupportsLive2D, device.SupportsLockScreen,
            device.Displays.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.NativeId).Select(d =>
                new DisplayState(d.Id, d.NativeId, d.Name, d.Width, d.Height, d.Scale, d.Rotation,
                    d.OffsetX, d.OffsetY, d.IsPrimary, d.IsConnected,
                    assignments.Where(x => x.DeviceDisplayId == d.Id).Select(x =>
                        new AssignmentResponse(x.Target, x.DesiredAssetId, x.ReportedAssetId,
                            x.Observation, x.Sequence, x.LastReportedAt, pendingRemoval.Any(c => c.DeviceDisplayId == x.DeviceDisplayId && c.Target == x.Target && c.Sequence == x.Sequence))).ToList(), d.RefreshRateHz, d.CanApplyWallpaper)).ToList(),
            device.SupportsWallpaperRemoval);
    }

    public async Task<DeviceState> HeartbeatAsync(AgentIdentity agent, AgentHeartbeat request, CancellationToken ct)
    {
        var device = await Linked(agent, ct);
        if (request.Revision <= device.InventoryRevision)
            throw new ConflictException("이전 기기 상태입니다. inventoryRevision보다 큰 revision을 전송하세요.");
        if (request.Revision < 1 || string.IsNullOrWhiteSpace(request.OsVersion) || request.OsVersion.Length > 120 ||
            string.IsNullOrWhiteSpace(request.AgentVersion) || request.AgentVersion.Length > 40 ||
            request.Displays is null || request.Displays.Count > 16 || request.Displays.Any(x => x is null) ||
            request.Displays.Select(x => x.NativeId).Distinct(StringComparer.Ordinal).Count() != request.Displays.Count ||
            request.Displays.Count(x => x.IsConnected && x.IsPrimary) > 1)
            throw new ValidationException("기기 또는 화면 목록이 올바르지 않습니다.");

        // Validate the entire report before changing tracked entities.
        foreach (var display in request.Displays)
        {
            if (string.IsNullOrWhiteSpace(display.NativeId) || display.NativeId.Length > 200 ||
                string.IsNullOrWhiteSpace(display.Name) || display.Name.Length > 120 ||
                display.Width is < 1 or > 32768 || display.Height is < 1 or > 32768 ||
                !double.IsFinite(display.Scale) || display.Scale is < 0.25 or > 8 ||
                (display.RefreshRateHz.HasValue && (!double.IsFinite(display.RefreshRateHz.Value) ||
                    display.RefreshRateHz is < 1 or > 1000)) ||
                display.Rotation is not (0 or 90 or 180 or 270) ||
                (display.Wallpapers?.Count ?? 0) > 3 || display.Wallpapers?.Any(x => x is null) == true ||
                display.Wallpapers?.Select(x => x.Target).Distinct().Count() != display.Wallpapers?.Count)
                throw new ValidationException("화면 정보가 올바르지 않습니다.");
            foreach (var report in display.Wallpapers ?? [])
            {
                ValidateTarget(device, report.Target, request.SupportsLockScreen);
                if (!Enum.IsDefined(report.Observation) ||
                    (report.Observation == WallpaperObservation.Managed) != report.AssetId.HasValue)
                    throw new ValidationException("관리 배경화면의 식별자와 관측 상태가 일치하지 않습니다.");
                if (report.AssetId.HasValue && !await db.WallpaperAssets.AnyAsync(
                        x => x.Id == report.AssetId && x.UserId == agent.UserId, ct))
                    throw new NotFoundException("배경화면을 찾을 수 없습니다.");
            }
        }

        device.OsVersion = request.OsVersion.Trim();
        device.AgentVersion = request.AgentVersion.Trim();
        // Clear legacy hardware metadata when a device next reports; older agents' JSON fields are ignored.
        device.Manufacturer = "";
        device.Model = "";
        device.Architecture = "";
        device.SupportsLive2D = request.SupportsLive2D;
        device.SupportsLockScreen = request.SupportsLockScreen;
        device.SupportsWallpaperRemoval = request.SupportsWallpaperRemoval;
        device.InventoryRevision = request.Revision;
        device.LastSeenAt = Now;
        foreach (var old in device.Displays) { old.IsConnected = false; old.IsPrimary = false; }
        foreach (var item in request.Displays)
        {
            var display = device.Displays.SingleOrDefault(x => x.NativeId == item.NativeId);
            if (display is null)
            {
                display = new DeviceDisplay { Device = device, DeviceId = device.Id, NativeId = item.NativeId };
                db.DeviceDisplays.Add(display);
            }
            display.Name = item.Name.Trim(); display.Width = item.Width; display.Height = item.Height;
            display.Scale = item.Scale; display.Rotation = item.Rotation;
            display.OffsetX = item.OffsetX; display.OffsetY = item.OffsetY;
            display.IsPrimary = item.IsPrimary; display.IsConnected = item.IsConnected;
            display.RefreshRateHz = item.RefreshRateHz;
            display.CanApplyWallpaper = item.CanApplyWallpaper;
            foreach (var report in item.Wallpapers ?? [])
            {
                var assignment = await Assignment(display, report.Target, ct);
                assignment.Observation = report.Observation;
                assignment.ReportedAssetId = report.AssetId;
                assignment.LastReportedAt = Now;
            }
        }
        await Save(agent.UserId, agent.DeviceId, ct);
        return await GetStateAsync(agent.UserId, agent.DeviceId, ct);
    }

    public async Task<DeviceCommandResponse> ApplyAsync(Guid userId, Guid deviceId, ApplyWallpaperRequest request, CancellationToken ct)
    {
        var device = await Owned(userId, deviceId, ct);
        if (device.AgentTokenHash is null) throw new ConflictException("기기를 먼저 연결하세요.");
        if (request.RequestId == Guid.Empty) throw new ValidationException("requestId가 필요합니다.");
        var prior = await db.DeviceCommands.SingleOrDefaultAsync(x => x.DeviceId == deviceId && x.RequestId == request.RequestId, ct);
        if (prior != null)
        {
            if (prior.Action != DeviceCommandAction.Apply || prior.DeviceDisplayId != request.DisplayId || prior.WallpaperAssetId != request.AssetId || prior.Target != request.Target)
                throw new ConflictException("동일 requestId를 다른 요청에 사용할 수 없습니다.");
            return Map(prior);
        }
        var display = device.Displays.SingleOrDefault(x => x.Id == request.DisplayId && x.IsConnected)
            ?? throw new NotFoundException("연결된 화면을 찾을 수 없습니다.");
        if (!display.CanApplyWallpaper) throw new ConflictException("이 화면은 정보 조회만 지원합니다.");
        var asset = await db.WallpaperAssets.SingleOrDefaultAsync(
            x => x.Id == request.AssetId && x.UserId == userId && !x.IsArchived, ct)
            ?? throw new NotFoundException("내 배경화면을 찾을 수 없습니다.");
        ValidateTarget(device, request.Target, device.SupportsLockScreen);
        if (asset.Mode == WallpaperMode.Live2D && !device.SupportsLive2D)
            throw new ConflictException("이 기기는 Live 2D를 지원하지 않습니다.");
        var previous = await db.DeviceCommands.Where(x => x.DeviceId == deviceId &&
            x.DeviceDisplayId == display.Id && x.Target == request.Target).ToListAsync(ct);
        foreach (var command in previous)
            if (Active(command)) command.Status = DeviceCommandStatus.Superseded;
        var next = new DeviceCommand { Device = device, DeviceDisplay = display, WallpaperAsset = asset,
            Target = request.Target, RequestId = request.RequestId, Sequence = ++device.CommandSequence,
            ExpiresAt = Now.AddDays(1) };
        db.DeviceCommands.Add(next);
        var assignment = await Assignment(display, request.Target, ct);
        assignment.DesiredAssetId = asset.Id; assignment.Sequence = next.Sequence;
        await Save(userId, deviceId, ct, notifyAgent: true);
        return Map(next);
    }

    public async Task<DeviceCommandResponse> ClearAsync(Guid userId, Guid deviceId, ClearWallpaperRequest request, CancellationToken ct)
    {
        var device = await Owned(userId, deviceId, ct);
        if (device.AgentTokenHash is null) throw new ConflictException("기기를 먼저 연결해 주세요.");
        if (request.RequestId == Guid.Empty) throw new ValidationException("requestId가 필요합니다.");
        var prior = await db.DeviceCommands.SingleOrDefaultAsync(x => x.DeviceId == deviceId && x.RequestId == request.RequestId, ct);
        if (prior != null)
        {
            if (prior.Action != DeviceCommandAction.Clear || prior.DeviceDisplayId != request.DisplayId || prior.Target != request.Target)
                throw new ConflictException("동일 requestId를 다른 요청에 사용할 수 없습니다.");
            return Map(prior);
        }
        if (!device.SupportsWallpaperRemoval) throw new ConflictException("연결 앱을 최신 버전으로 업데이트한 뒤 배경화면을 삭제해 주세요.");
        var display = device.Displays.SingleOrDefault(x => x.Id == request.DisplayId && x.IsConnected)
            ?? throw new NotFoundException("연결된 화면을 찾을 수 없습니다.");
        if (!display.CanApplyWallpaper) throw new ConflictException("이 화면은 정보 조회만 지원합니다.");
        ValidateTarget(device, request.Target, device.SupportsLockScreen);
        foreach (var old in await db.DeviceCommands.Where(x => x.DeviceId == deviceId && x.DeviceDisplayId == display.Id && x.Target == request.Target).ToListAsync(ct))
            if (Active(old)) old.Status = DeviceCommandStatus.Superseded;
        var command = new DeviceCommand { Device = device, DeviceDisplay = display, Action = DeviceCommandAction.Clear,
            Target = request.Target, RequestId = request.RequestId, Sequence = ++device.CommandSequence, ExpiresAt = Now.AddDays(1) };
        db.DeviceCommands.Add(command);
        var assignment = await Assignment(display, request.Target, ct);
        assignment.DesiredAssetId = null; assignment.Sequence = command.Sequence;
        await Save(userId, deviceId, ct, notifyAgent: true);
        return Map(command);
    }

    public async Task<IReadOnlyList<DeviceCommandResponse>> GetPendingCommandsAsync(Guid userId, Guid deviceId, CancellationToken ct)
    {
        var device = await Owned(userId, deviceId, ct);
        var query = db.DeviceCommands.Where(x => x.DeviceId == deviceId);
        if (!device.SupportsWallpaperRemoval) query = query.Where(x => x.Action != DeviceCommandAction.Clear);
        query = query.Where(x => x.Status == DeviceCommandStatus.Pending ||
            x.Status == DeviceCommandStatus.Downloading || x.Status == DeviceCommandStatus.NeedsUserAction);
        var commands = await query.OrderByDescending(x => x.Sequence).Take(100).ToListAsync(ct);
        foreach (var command in commands)
            if (Active(command) && command.ExpiresAt <= Now) command.Status = DeviceCommandStatus.Expired;
        device.LastSeenAt = Now;
        await db.SaveChangesAsync(ct);
        return commands.Where(Active).Select(Map).ToList();
    }

    public async Task<DeviceCommandResponse> ReportAsync(AgentIdentity agent, Guid commandId, CommandReport report, CancellationToken ct)
    {
        var device = await Linked(agent, ct);
        var command = await db.DeviceCommands.SingleOrDefaultAsync(x => x.Id == commandId && x.DeviceId == device.Id, ct)
            ?? throw new NotFoundException("명령을 찾을 수 없습니다.");
        if (report.Status is not (DeviceCommandStatus.Downloading or DeviceCommandStatus.NeedsUserAction or
            DeviceCommandStatus.Applied or DeviceCommandStatus.Failed) || report.Error?.Length > 1000)
            throw new ValidationException("기기가 보고할 수 없는 명령 상태입니다.");
        if (!Active(command))
        {
            if (command.Status == report.Status && command.Status is DeviceCommandStatus.Applied or DeviceCommandStatus.Failed)
                return Map(command);
            throw new ConflictException("이미 종료되거나 대체된 명령입니다.");
        }
        if (command.ExpiresAt <= Now) throw new ConflictException("명령이 만료되었습니다.");
        var assignment = await db.WallpaperAssignments.SingleAsync(x =>
            x.DeviceDisplayId == command.DeviceDisplayId && x.Target == command.Target, ct);
        if (assignment.Sequence != command.Sequence) throw new ConflictException("최신 적용 명령이 아닙니다.");
        command.Status = report.Status; command.Error = report.Error;
        device.LastSeenAt = Now;
        // Heartbeats and command reports share one optimistic concurrency boundary.
        if (report.Status == DeviceCommandStatus.Applied)
        {
            assignment.ReportedAssetId = command.WallpaperAssetId;
            assignment.Observation = command.Action == DeviceCommandAction.Clear ? WallpaperObservation.Cleared : WallpaperObservation.Managed;
            assignment.LastReportedAt = Now;
        }
        await Save(agent.UserId, agent.DeviceId, ct);
        return Map(command);
    }

    public async Task<Guid> GetCommandAssetAsync(AgentIdentity agent, Guid commandId, CancellationToken ct)
    {
        var command = await GetCommandAsync(agent, commandId, ct);
        if (command.Action != DeviceCommandAction.Apply || !command.AssetId.HasValue)
            throw new ConflictException("배경화면 삭제 명령에는 이미지 파일이 없습니다.");
        return command.AssetId.Value;
    }

    public async Task<DeviceCommandResponse> GetCommandAsync(AgentIdentity agent, Guid commandId, CancellationToken ct)
    {
        await Linked(agent, ct);
        var command = await db.DeviceCommands.Include(x => x.DeviceDisplay).SingleOrDefaultAsync(x => x.Id == commandId && x.DeviceId == agent.DeviceId, ct)
            ?? throw new NotFoundException("명령을 찾을 수 없습니다.");
        if ((!Active(command) && command.Status != DeviceCommandStatus.Applied) || (Active(command) && command.ExpiresAt <= Now) ||
            !await db.WallpaperAssignments.AnyAsync(x => x.DeviceDisplayId == command.DeviceDisplayId &&
                x.Target == command.Target && x.Sequence == command.Sequence, ct))
            throw new ConflictException("더 이상 적용할 수 없는 명령입니다.");
        if (!command.DeviceDisplay.IsConnected || !command.DeviceDisplay.CanApplyWallpaper)
            throw new ConflictException("적용 대상 화면을 사용할 수 없습니다.");
        return Map(command);
    }

    private async Task<Device> Owned(Guid userId, Guid deviceId, CancellationToken ct) =>
        await db.Devices.Include(x => x.Displays).SingleOrDefaultAsync(x => x.Id == deviceId && x.UserId == userId, ct)
        ?? throw new NotFoundException("기기를 찾을 수 없습니다.");

    private async Task<Device> Linked(AgentIdentity agent, CancellationToken ct)
    {
        var device = await Owned(agent.UserId, agent.DeviceId, ct);
        if (device.AgentTokenHash is null) throw new UnauthorizedException("기기 연결이 해제되었습니다.");
        return device;
    }

    private async Task<WallpaperAssignment> Assignment(DeviceDisplay display, WallpaperTarget target, CancellationToken ct)
    {
        var assignment = db.WallpaperAssignments.Local.SingleOrDefault(x => x.DeviceDisplayId == display.Id && x.Target == target)
            ?? await db.WallpaperAssignments.SingleOrDefaultAsync(x => x.DeviceDisplayId == display.Id && x.Target == target, ct);
        if (assignment != null) return assignment;
        assignment = new() { DeviceDisplay = display, DeviceDisplayId = display.Id, Target = target };
        db.WallpaperAssignments.Add(assignment);
        return assignment;
    }

    private static void ValidateTarget(Device device, WallpaperTarget target, bool lockScreen)
    {
        if (!Enum.IsDefined(target) || (device.Platform == DevicePlatform.Windows && target != WallpaperTarget.Desktop) ||
            (device.Platform == DevicePlatform.Android && (target == WallpaperTarget.Desktop ||
                (target == WallpaperTarget.Lock && !lockScreen))))
            throw new ValidationException("기기에서 지원하지 않는 배경화면 대상입니다.");
    }

    private async Task Save(Guid userId, Guid deviceId, CancellationToken ct, bool notifyAgent = false)
    {
        var device = db.Devices.Local.Single(x => x.Id == deviceId);
        device.ControlVersion = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        if (events != null) await events.ChangedAsync(userId, deviceId, ct, notifyAgent);
    }

    private DeviceCommandResponse Map(DeviceCommand c) => new(c.Id, c.DeviceId, c.DeviceDisplayId,
        c.WallpaperAssetId, c.Target, c.RequestId, c.Sequence,
        Active(c) && c.ExpiresAt <= Now ? DeviceCommandStatus.Expired : c.Status, c.ExpiresAt, c.Error, c.Action);
}

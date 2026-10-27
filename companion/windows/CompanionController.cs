using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace UpscaleLab.Companion;

internal sealed class CompanionController : IDisposable
{
    private readonly HttpClient requests = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient downloads = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(3) };
    private readonly WallpaperEngine engine = new();
    private AgentApi? api;
    private CancellationTokenSource? cancellation;
    private Task? loop;
    private bool remember;
    private bool verified;
    private Dictionary<string, AppliedWallpaper> applied = new();
    public event Action? Changed;
    public string Status { get; private set; } = "웹과 같은 계정으로 로그인해 주세요.";
    public string Account => api?.Session?.User.Email ?? "";
    public string Server => api?.Server ?? SecureStore.Read()?.Server ?? "";
    public bool LoggedIn => verified;
    public bool Enabled { get; private set; }
    public string DeviceName => Environment.MachineName;
    public MonitorInfo[] Monitors { get; private set; } = [];
    private string AppliedFile => Path.Combine(SecureStore.Root, $"applied-{api!.Session!.User.Id:N}-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(api.Server)))[..12]}.json");
    private void Notify(string status) { Status = status; Changed?.Invoke(); }
    private void Persist() { if (api != null) SecureStore.Write(new(api.Server, api.Session, api.Device, Enabled, remember)); }
    private void SetApi(string server, Login? session, Binding? binding)
    {
        api = new AgentApi(server, requests) { Session = session, Device = binding };
        api.SessionChanged = Persist;
    }
    public async Task Restore()
    {
        var saved = SecureStore.Read(); if (saved?.Session is null || !saved.Remember) return;
        remember = true; SetApi(saved.Server, saved.Session, saved.Device);
        try
        {
            await api!.Call<Account>("/api/auth/me"); verified = true;
            LoadApplied(); if (saved.Enabled) await Enable(); else Notify("동기화가 꺼져 있습니다.");
        }
        catch (Exception error) { Notify("자동 연결 대기: " + error.Message); }
    }
    public async Task SignIn(string server, string identifier, string password, bool automatic)
    {
        await Stop(); verified = false;
        var saved = SecureStore.Read(); SetApi(server, null, null);
        var login = await api!.Call<Login>("/api/auth/login", HttpMethod.Post, new { email = identifier.Trim(), password }, anonymous: true);
        if (string.IsNullOrWhiteSpace(login.RefreshToken)) throw new InvalidOperationException("계정 세션을 지원하는 서버가 필요합니다.");
        api.Session = login; remember = automatic; verified = true;
        if (saved?.Device is { } binding && binding.Server == api.Server && binding.UserId == login.User.Id) api.Device = binding;
        LoadApplied(); await Enable();
    }
    public async Task Reconnect()
    {
        await Stop(); if (!verified || api?.Session is null) return;
        await Bind(force: true); await Enable();
    }
    private sealed record Registered(Guid Id);
    private sealed record IssuedToken(Guid DeviceId, string Token);
    private async Task Bind(bool force = false)
    {
        if (api is null || api.Session is null) return;
        if (api.Device != null && !force) return;
        Monitors = DisplayInventory.Read(); var primary = Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors.FirstOrDefault()
            ?? throw new InvalidOperationException("연결된 화면을 찾지 못했습니다.");
        Guid id = api.Device?.DeviceId ?? Guid.Empty;
        if (id != Guid.Empty)
        {
            try { var token = await api.Call<IssuedToken>($"/api/devices/{id}/agent-token", HttpMethod.Post, new { });
                api.Device = new(api.Server, api.Session.User.Id, id, token.Token); Persist(); return; }
            catch (ApiFailure error) when (error.Status == 404) { }
        }
        var device = await api.Call<Registered>("/api/devices", HttpMethod.Post, new { platform = "Windows", deviceName = DeviceName,
            screenWidth = primary.Width, screenHeight = primary.Height, aspectRatio = $"{primary.Width}:{primary.Height}",
            orientation = primary.Width >= primary.Height ? "Landscape" : "Portrait" });
        var issued = await api.Call<IssuedToken>($"/api/devices/{device.Id}/agent-token", HttpMethod.Post, new { });
        api.Device = new(api.Server, api.Session.User.Id, device.Id, issued.Token); Persist();
    }
    public async Task Enable()
    {
        if (Enabled || !verified || api?.Session is null) return;
        await Bind(); Enabled = true; Persist(); cancellation = new(); loop = Run(cancellation.Token);
    }
    public void RefreshInventory() { Monitors = DisplayInventory.Read(); Changed?.Invoke(); }
    public async Task Stop()
    {
        Enabled = false; cancellation?.Cancel();
        if (loop != null) try { await loop; } catch (OperationCanceledException) { }
        cancellation?.Dispose(); cancellation = null; loop = null; engine.Dispose();
        Persist(); Notify("동기화가 꺼져 있습니다.");
    }
    public async Task Logout()
    {
        await Stop(); var previous = api?.Session; if (api != null) api.Session = null; verified = false; Persist();
        if (previous != null && api != null)
            try { await api.Call<object>("/api/auth/logout", HttpMethod.Post, new { refreshToken = previous.RefreshToken }, anonymous: true); }
            catch (Exception) { /* Local credentials and synchronization have already been removed. */ }
        Notify("로그아웃했습니다. 적용된 정적 배경화면은 기기에 남습니다.");
    }
    private void LoadApplied()
    {
        try { applied = JsonSerializer.Deserialize<Dictionary<string, AppliedWallpaper>>(File.ReadAllText(AppliedFile), AgentApi.Json) ?? new(); }
        catch (Exception error) when (error is IOException or JsonException) { applied = new(); }
    }
    private void SaveApplied() { Directory.CreateDirectory(SecureStore.Root); File.WriteAllText(AppliedFile + ".tmp", JsonSerializer.Serialize(applied, AgentApi.Json)); File.Move(AppliedFile + ".tmp", AppliedFile, true); }
    private async Task<AgentState> State(CancellationToken ct) => await api!.Call<AgentState>("/api/agent/state", agent: true, ct: ct);
    private async Task Heartbeat(CancellationToken ct)
    {
        Monitors = DisplayInventory.Read(); var state = await State(ct);
        var body = new { revision = state.InventoryRevision + 1, osVersion = RuntimeInformation.OSDescription,
            agentVersion = typeof(CompanionController).Assembly.GetName().Version?.ToString(3) ?? "1.0.1", manufacturer = DisplayInventory.Hardware("SystemManufacturer"),
            model = DisplayInventory.Hardware("SystemProductName"), architecture = RuntimeInformation.OSArchitecture.ToString(),
            supportsLive2D = engine.SupportsLive, supportsLockScreen = false,
            displays = Monitors.Select(m => new { m.NativeId, m.Name, m.Width, m.Height, m.Scale, m.Rotation, m.OffsetX, m.OffsetY,
                m.IsPrimary, m.IsConnected, m.RefreshRateHz,
                wallpapers = new[] { applied.TryGetValue(m.NativeId, out var a) && engine.Matches(m, a)
                    ? new { target = "Desktop", observation = "Managed", assetId = (Guid?)a.AssetId }
                    : new { target = "Desktop", observation = "External", assetId = (Guid?)null } } }).ToArray() };
        try { await api!.Call<AgentState>("/api/agent/heartbeat", HttpMethod.Post, body, agent: true, ct: ct); }
        catch (ApiFailure error) when (error.Status == 409) { /* The next loop re-reads revision. */ }
    }
    private async Task Run(CancellationToken ct)
    {
        var nextHeartbeat = DateTime.MinValue; var restore = true;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow >= nextHeartbeat) { await Heartbeat(ct); nextHeartbeat = DateTime.UtcNow.AddSeconds(30); }
                var cache = new WallpaperCache(downloads, api!.Session!.User.Id, api.Server);
                if (restore)
                {
                    foreach (var (native, saved) in applied.Where(pair => pair.Value.Mode == "Live2D").ToArray())
                    {
                        var monitor = Monitors.FirstOrDefault(m => m.NativeId == native);
                        if (monitor == null || !engine.Matches(monitor, saved)) continue;
                        try { var manifest = await api.Call<WallpaperManifest>($"/api/agent/commands/{saved.CommandId}/manifest", agent: true, ct: ct);
                            engine.Apply(monitor, await cache.Download(manifest, ct)); }
                        catch (ApiFailure error) when (error.Status is 404 or 409) { }
                    }
                    restore = false;
                }
                var commands = await api.Call<DeviceCommand[]>("/api/agent/commands", agent: true, ct: ct);
                foreach (var command in commands.OrderBy(c => c.Sequence))
                {
                    var didApply = false;
                    try
                    {
                        if (command.Target != "Desktop") throw new InvalidOperationException("지원하지 않는 배경화면 대상입니다.");
                        await api.Report(command.Id, "Downloading", null, ct);
                        var manifest = await api.Call<WallpaperManifest>($"/api/agent/commands/{command.Id}/manifest", agent: true, ct: ct);
                        if (manifest.AssetId != command.AssetId) throw new InvalidDataException("결과물 식별자가 일치하지 않습니다.");
                        var local = await cache.Download(manifest, ct);
                        var fresh = await State(ct);
                        var display = fresh.Displays.SingleOrDefault(d => d.Id == command.DisplayId && d.IsConnected)
                            ?? throw new InvalidOperationException("화면 연결이 해제되었습니다.");
                        if (!display.Wallpapers.Any(a => a.Target == "Desktop" && a.Sequence == command.Sequence && a.DesiredAssetId == command.AssetId)) continue;
                        var monitor = Monitors.SingleOrDefault(m => m.NativeId == display.NativeId)
                            ?? throw new InvalidOperationException("연결된 모니터를 찾지 못했습니다.");
                        // Revalidate expiration/revocation after downloading; state alone retains expired desired assignments.
                        await api.Call<WallpaperManifest>($"/api/agent/commands/{command.Id}/manifest", agent: true, ct: ct);
                        ct.ThrowIfCancellationRequested();
                        if (!applied.TryGetValue(monitor.NativeId, out var old) || old.CommandId != command.Id || !engine.Matches(monitor, old))
                        {
                            engine.Apply(monitor, local);
                            didApply = true;
                            applied[monitor.NativeId] = new(command.AssetId, command.Id, engine.CurrentPath, manifest.Mode); SaveApplied();
                        }
                        didApply = true; await api.Report(command.Id, "Applied", null, ct); Notify("배경화면을 적용했습니다. " + DateTime.Now.ToString("HH:mm:ss"));
                    }
                    catch (ApiFailure error) when (error.Status is 404 or 409) { }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception error)
                    {
                        if (error is ApiFailure { Status: 401 }) throw;
                        if (!didApply && error is not (HttpRequestException or TaskCanceledException) &&
                            (error is not IOException || error is InvalidDataException))
                            try { await api.Report(command.Id, "Failed", error.Message[..Math.Min(error.Message.Length, 900)], ct); } catch { }
                        Notify(didApply ? "적용 결과 보고를 다시 시도합니다." : error.Message);
                    }
                }
                Changed?.Invoke();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (ApiFailure error) when (error.Status == 401)
            { Enabled = false; engine.Dispose(); Persist(); Notify("계정 또는 기기 연결을 다시 확인해 주세요. [기기 다시 연결]을 눌러 주세요."); return; }
            catch (Exception error) { engine.Dispose(); restore = true; Notify("연결 재시도 중: " + error.Message); }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
    public void Dispose() { cancellation?.Cancel(); engine.Dispose(); requests.Dispose(); downloads.Dispose(); }
}

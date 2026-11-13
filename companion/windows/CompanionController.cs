using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace UpscaleLab.Companion;

internal sealed class CompanionController : IDisposable
{
    private readonly HttpClient requests;
    private readonly Func<bool> privacyAccepted;
    private readonly Func<MonitorInfo[]> readDisplays;
    private readonly Func<Credentials?> readCredentials;
    private readonly Action<Credentials> writeCredentials;
    private readonly HttpClient downloads = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(3) };
    private readonly WallpaperEngine engine = new();
    private AgentApi? api;
    private CancellationTokenSource? cancellation;
    private Task? loop;
    private Task? watcher;
    private readonly SemaphoreSlim wake = new(0, 1);
    private bool inventoryDirty;
    private SynchronizationContext? ui;
    private bool remember;
    private bool verified;
    private Dictionary<string, AppliedWallpaper> applied = new();
    internal CompanionController(HttpClient? requests = null, Func<bool>? privacyAccepted = null, Func<MonitorInfo[]>? readDisplays = null,
        Func<Credentials?>? readCredentials = null, Action<Credentials>? writeCredentials = null)
    {
        this.requests = requests ?? new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        this.privacyAccepted = privacyAccepted ?? (() => PrivacyConsent.Current.Accepted);
        this.readDisplays = readDisplays ?? DisplayInventory.Read;
        this.readCredentials = readCredentials ?? SecureStore.Read;
        this.writeCredentials = writeCredentials ?? SecureStore.Write;
    }
    private void RequireConsent() {
        if (!privacyAccepted()) throw new InvalidOperationException("기기·화면 정보 수집·이용 동의가 필요합니다.");
    }
    private MonitorInfo[] ReadDisplays() {
        RequireConsent();
        if (!verified || api?.Session is null) throw new InvalidOperationException("로그인이 필요합니다.");
        return readDisplays();
    }
    public event Action? Changed;
    public string Status { get; private set; } = "웹과 같은 계정으로 로그인해 주세요.";
    public string Account => api?.Session?.User.Email ?? "";
    public string Username => Profile?.Username ?? api?.Session?.User.Username ?? "";
    public AccountProfile? Profile { get; private set; }
    public string Plan { get; private set; } = "확인 중";
    public ConnectionState Connection { get; private set; }
    public string Server => CompanionConfiguration.ApiBaseUrl;
    public bool LoggedIn => verified;
    public bool Enabled { get; private set; }
    public string DeviceName => Environment.MachineName;
    public MonitorInfo[] Monitors { get; private set; } = [];
    private string AppliedFile => Path.Combine(SecureStore.Root, $"applied-{api!.Session!.User.Id:N}-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(api.Server)))[..12]}.json");
    private void Notify(string status) { Status = status; Changed?.Invoke(); }
    public void UpdatePlayback(PlaybackRules rules, bool displaySleeping) => engine.UpdatePlayback(rules, displaySleeping);

    private void Persist() { if (api != null) writeCredentials(new(api.Server, api.Session, api.Device, Enabled, remember)); }
    private void SetApi(string server, Login? session, Binding? binding)
    {
        api = new AgentApi(server, requests) { Session = session, Device = binding };
        api.SessionChanged = Persist;
    }
    public async Task Restore()
    {
        if (!privacyAccepted()) return;
        var saved = readCredentials(); if (saved is null || !CompanionConfiguration.CanRestore(saved, Server)) return;
        remember = true; SetApi(Server, saved.Session, saved.Device);
        try
        {
            await api!.Call<Account>("/api/auth/me"); verified = true;
            LoadApplied(); await Enable(); await RefreshProfile();
        }
        catch (Exception error) { Notify("자동 연결 대기: " + error.Message); }
    }
    public async Task SignIn(string identifier, string password, bool automatic)
    {
        RequireConsent();
        await Stop(); verified = false; Monitors = []; Profile = null; Plan = "확인 중";
        Connection = ConnectionState.Connecting; Notify("로그인 중입니다.");
        var saved = readCredentials(); SetApi(Server, null, null);
        var login = await api!.Call<Login>("/api/auth/login", HttpMethod.Post, new { email = identifier.Trim(), password }, anonymous: true);
        if (string.IsNullOrWhiteSpace(login.RefreshToken)) throw new InvalidOperationException("계정 세션을 지원하는 서버가 필요합니다.");
        api.Session = login; remember = automatic; verified = true;
        if (saved?.Device is { } binding && binding.Server == api.Server && binding.UserId == login.User.Id) api.Device = binding;
        LoadApplied(); await Bind(force: true); await Enable(); await RefreshProfile();
    }
    public async Task RefreshProfile() {
        if (!verified || api?.Session is null) return;
        var owner = api.Session.User.Id;
        try {
            var profile = await api.Call<AccountProfile>("/api/account");
            if (api.Session?.User.Id != owner || !verified) return;
            if (profile.Id != owner) throw new InvalidDataException("계정 정보가 일치하지 않습니다.");
            Profile = profile;
            var wallet = await api.Call<AccountWallet>("/api/tokens/me");
            if (api.Session?.User.Id == owner && verified) Plan = wallet.Plan;
        } catch (Exception e) when (e is HttpRequestException or ApiFailure or TaskCanceledException or InvalidDataException) { Plan = "확인 불가"; }
        Changed?.Invoke();
    }
    public async Task<Image?> LoadAvatar(string url) {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return null;
        using var response = await downloads.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 6 * 1024 * 1024) return null;
        using var source = await response.Content.ReadAsStreamAsync(); using var bytes = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = await source.ReadAsync(buffer)) > 0) { if (bytes.Length + count > 6 * 1024 * 1024) return null; bytes.Write(buffer, 0, count); }
        bytes.Position = 0; using var image = new ImageMagick.MagickImage(bytes); image.Resize(128, 128);
        using var png = new MemoryStream(image.ToByteArray(ImageMagick.MagickFormat.Png));
        using var decoded = Image.FromStream(png); return new Bitmap(decoded);
    }
    public void ConnectionFailed() { Connection = ConnectionState.Disconnected; Changed?.Invoke(); }
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
        Monitors = ReadDisplays(); var primary = Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors.FirstOrDefault()
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
        RequireConsent();
        if (Enabled || !verified || api?.Session is null) return;
        Connection = ConnectionState.Connecting;
        await Bind(); Enabled = true; Persist(); cancellation = new(); inventoryDirty = true; ui = SynchronizationContext.Current;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += PreferencesChanged;
        watcher = Watch(cancellation.Token); loop = Run(cancellation.Token);
    }
    private void DisplayChanged(object? sender, EventArgs e) => InventoryChanged();
    private void PreferencesChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) {
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.Desktop) InventoryChanged();
    }
    private void InventoryChanged() {
        void Signal(object? _) { inventoryDirty = true; Wake(); }
        if (ui != null) ui.Post(Signal, null); else Signal(null);
    }
    private void Wake() { if (wake.CurrentCount == 0) try { wake.Release(); } catch (SemaphoreFullException) { } }
    private sealed record UpdateNotice(Guid Cursor);
    private async Task Watch(CancellationToken ct) {
        Guid? cursor = null;
        while (!ct.IsCancellationRequested) {
            try {
                var notice = await api!.Call<UpdateNotice>("/api/agent/updates" + (cursor.HasValue ? $"?after={cursor}" : ""), agent: true, ct: ct);
                if (notice.Cursor != cursor) { cursor = notice.Cursor; Wake(); }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception) { Wake(); try { await Task.Delay(3000, ct); } catch (OperationCanceledException) { return; } }
        }
    }
    public void RefreshInventory() { Monitors = verified && privacyAccepted() ? ReadDisplays() : []; Changed?.Invoke(); }
    public async Task Stop()
    {
        Enabled = false; Connection = ConnectionState.Disconnected; cancellation?.Cancel();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= PreferencesChanged;
        if (loop != null) try { await loop; } catch (OperationCanceledException) { }
        if (watcher != null) try { await watcher; } catch (OperationCanceledException) { }
        watcher = null;
        cancellation?.Dispose(); cancellation = null; loop = null; engine.Dispose();
        Persist(); Notify("동기화가 꺼져 있습니다.");
    }
    public async Task Logout()
    {
        await Stop(); var previous = api?.Session; if (api != null) api.Session = null; verified = false; Monitors = []; Profile = null; Plan = "확인 중"; Persist();
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
        Monitors = ReadDisplays(); var state = await State(ct);
        var body = new { revision = state.InventoryRevision + 1, osVersion = RuntimeInformation.OSDescription,
            agentVersion = typeof(CompanionController).Assembly.GetName().Version?.ToString(3) ?? "1.0.1",
            supportsLive2D = engine.SupportsLive, supportsLockScreen = false, supportsWallpaperRemoval = true,
            displays = Monitors.Select(m => new { m.NativeId, m.Name, m.Width, m.Height, m.Scale, m.Rotation, m.OffsetX, m.OffsetY,
                m.IsPrimary, m.IsConnected, m.RefreshRateHz,
                wallpapers = new[] { applied.TryGetValue(m.NativeId, out var a) && engine.Matches(m, a)
                    ? new { target = "Desktop", observation = "Managed", assetId = (Guid?)a.AssetId }
                    : new { target = "Desktop", observation = engine.IsClear(m) ? "Cleared" : "External", assetId = (Guid?)null } } }).ToArray() };
        try { await api!.Call<AgentState>("/api/agent/heartbeat", HttpMethod.Post, body, agent: true, ct: ct); }
        catch (ApiFailure error) when (error.Status == 409) { /* The next loop re-reads revision. */ }
    }
    private async Task Run(CancellationToken ct)
    {
        var nextHeartbeat = DateTime.MinValue; var restore = true;
        while (!ct.IsCancellationRequested)
        {
            if (Connection != ConnectionState.Connected) { Connection = ConnectionState.Connecting; Changed?.Invoke(); }
            try
            {
                if (inventoryDirty || DateTime.UtcNow >= nextHeartbeat) { inventoryDirty = false; await Heartbeat(ct); nextHeartbeat = DateTime.UtcNow.AddSeconds(30); }
                var cache = new WallpaperCache(downloads, api!.Session!.User.Id, api.Server);
                if (restore)
                {
                    foreach (var (native, saved) in applied.Where(pair => pair.Value.Mode == "Live2D").ToArray())
                    {
                        var monitor = Monitors.FirstOrDefault(m => m.NativeId == native);
                        if (monitor == null || !engine.Matches(monitor, saved) || engine.IsLive(native)) continue;
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
                        if (command.Action == "Clear")
                        {
                            var freshClear = await State(ct);
                            var clearDisplay = freshClear.Displays.SingleOrDefault(d => d.Id == command.DisplayId && d.IsConnected)
                                ?? throw new InvalidOperationException("화면 연결이 해제되었습니다.");
                            if (!clearDisplay.Wallpapers.Any(a => a.Target == "Desktop" && a.Sequence == command.Sequence && a.DesiredAssetId == null)) continue;
                            var clearMonitor = Monitors.SingleOrDefault(m => m.NativeId == clearDisplay.NativeId)
                                ?? throw new InvalidOperationException("연결된 모니터를 찾지 못했습니다.");
                            var confirmed = await api.Call<DeviceCommand>($"/api/agent/commands/{command.Id}", agent: true, ct: ct);
                            if (confirmed.Action != "Clear" || confirmed.Sequence != command.Sequence) continue;
                            ct.ThrowIfCancellationRequested(); engine.Clear(clearMonitor); didApply = true;
                            if (applied.Remove(clearMonitor.NativeId, out var removed))
                            {
                                SaveApplied();
                                if (!applied.Values.Any(a => a.AssetId == removed.AssetId))
                                    try { cache.Remove(removed.AssetId); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                            }
                            await api.Report(command.Id, "Applied", null, ct); inventoryDirty = true;
                            Notify("배경화면을 삭제했습니다."); continue;
                        }
                        if (command.Action != "Apply" || !command.AssetId.HasValue) throw new InvalidDataException("잘못된 배경화면 명령입니다.");
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
                            applied[monitor.NativeId] = new(command.AssetId.Value, command.Id, engine.CurrentPath, manifest.Mode); SaveApplied();
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
                Connection = ConnectionState.Connected;
                if (Status == "웹과 같은 계정으로 로그인해 주세요." || Status == "로그인 중입니다." || Status.StartsWith("연결 재시도 중:")) Status = "연결됨 · 배경화면을 자동으로 동기화합니다.";
                Changed?.Invoke();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (ApiFailure error) when (error.Status == 401)
            { Enabled = false; Connection = ConnectionState.Disconnected; cancellation?.Cancel(); engine.Dispose(); Persist(); Notify("연결이 만료되었습니다. 웹의 기기 관리 또는 다시 로그인하여 확인해 주세요."); return; }
            catch (Exception error)
            {
                // The desktop is rendered from local layers. Keep it running while sync retries.
                Connection = ConnectionState.Disconnected;
                inventoryDirty = true;
                Notify("연결 재시도 중: " + error.Message);
            }
            await wake.WaitAsync(TimeSpan.FromSeconds(15), ct);
        }
    }
    public void Dispose() { cancellation?.Cancel(); Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= PreferencesChanged; engine.Dispose(); requests.Dispose(); downloads.Dispose(); }
}

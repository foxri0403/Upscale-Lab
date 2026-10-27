using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace UpscaleLab.Companion;

internal record Account(Guid Id, string Email, string Username);
internal record Login(string AccessToken, string RefreshToken, DateTime ExpiresAt, Account User);
internal record Binding(string Server, Guid UserId, Guid DeviceId, string Token);
internal record Credentials(string Server, Login? Session, Binding? Device, bool Enabled, bool Remember);
internal record AppliedWallpaper(Guid AssetId, Guid CommandId, string Path, string Mode);
internal record Assignment(string Target, Guid? DesiredAssetId, Guid? ReportedAssetId, string Observation, long Sequence);
internal record DisplayState(Guid Id, string NativeId, bool IsConnected, Assignment[] Wallpapers);
internal record AgentState(long InventoryRevision, DisplayState[] Displays);
internal record DeviceCommand(Guid Id, Guid DisplayId, Guid AssetId, string Target, long Sequence, string Status, DateTime ExpiresAt);
internal record WallpaperFile(string Source, string ContentType, int Order, string Role, double X = 0, double Y = 0,
    double Rotation = 0, double Scale = 1, double MovementX = 0, double MovementY = 0);
internal record WallpaperManifest(int SchemaVersion, Guid AssetId, string Mode, int Width, int Height, WallpaperFile[] Files);
internal sealed class ApiFailure(int status, string message) : Exception(message) { public int Status { get; } = status; }

internal sealed class AgentApi(string server, HttpClient http)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public Login? Session { get; set; }
    public Binding? Device { get; set; }
    public Action? SessionChanged { get; set; }
    public string Server { get; } = NormalizeServer(server);
    private readonly SemaphoreSlim refresh = new(1, 1);
    public static string NormalizeServer(string input)
    {
        if (!Uri.TryCreate(input.Trim().TrimEnd('/'), UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http") || !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("올바른 서버 주소를 입력해 주세요.");
        return uri.GetLeftPart(UriPartial.Authority);
    }
    public async Task<T> Call<T>(string path, HttpMethod? method = null, object? body = null,
        bool agent = false, bool anonymous = false, CancellationToken ct = default)
    {
        if (agent && (Session is null || Device is null || Device.UserId != Session.User.Id || Device.Server != Server))
            throw new ApiFailure(401, "같은 계정의 로그인과 기기 연결이 필요합니다.");
        async Task<HttpResponseMessage> Send()
        {
            using var request = new HttpRequestMessage(method ?? HttpMethod.Get, Server + path);
            if (body != null) request.Content = JsonContent.Create(body, options: Json);
            if (!anonymous && Session != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session.AccessToken);
            if (agent) request.Headers.Add("X-Device-Token", Device!.Token);
            return await http.SendAsync(request, ct);
        }
        var response = await Send();
        if (response.StatusCode == HttpStatusCode.Unauthorized && !anonymous && Session != null)
        {
            response.Dispose();
            await refresh.WaitAsync(ct);
            try
            {
                var previous = Session ?? throw new ApiFailure(401, "다시 로그인해 주세요.");
                var updated = await Call<Login>("/api/auth/refresh", HttpMethod.Post,
                    new { refreshToken = previous.RefreshToken }, anonymous: true, ct: ct);
                if (Session != previous) throw new OperationCanceledException();
                if (updated.User.Id != previous.User.Id) throw new ApiFailure(401, "로그인 계정이 일치하지 않습니다.");
                Session = updated;
                SessionChanged?.Invoke();
            }
            finally { refresh.Release(); }
            response = await Send();
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string message = response.StatusCode == HttpStatusCode.Unauthorized
                    ? "로그인 또는 기기 연결이 만료되었습니다. 다시 연결해 주세요." : "서버 요청에 실패했습니다.";
                try
                {
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    if (json.RootElement.TryGetProperty("message", out var value)) message = value.GetString() ?? message;
                }
                catch (JsonException) { }
                throw new ApiFailure((int)response.StatusCode, message);
            }
            if (response.StatusCode == HttpStatusCode.NoContent) return default!;
            return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
        }
    }
    public Task Report(Guid id, string status, string? error, CancellationToken ct) =>
        Call<object>($"/api/agent/commands/{id}/report", HttpMethod.Post, new { status, error }, agent: true, ct: ct);
}

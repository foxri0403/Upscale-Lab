using System.Text.Json;

namespace UpscaleLab.Companion;

internal sealed class PrivacyConsent(string file, string server)
{
    internal const int Version = 1;
    internal const string Title = "기기·화면 정보 수집·이용 동의";
    internal const string Details = "수집·이용 목적\n기기를 계정에 연결하고 웹에서 선택한 배경화면과 적용 상태를 동기화합니다.\n\n수집·전송 항목\n기기 이름, OS·앱 버전, 화면 이름·개수·해상도·주사율·배율·회전·위치·주 화면·연결 상태, 이 앱이 적용한 배경화면의 식별 정보와 적용 상태.\n\n외부 배경화면 사진, 다른 앱 목록, 제조사·모델·CPU 구조는 전송하지 않습니다. 동의 및 로그인 확인 후에만 화면을 감지합니다. 로그아웃하면 수집·동기화를 중지합니다.\n\n동의를 거부할 수 있으며, 거부하면 연결 앱을 사용할 수 없습니다.";
    internal static PrivacyConsent Current => new(Path.Combine(SecureStore.Root, "profiles", CompanionConfiguration.ServerId, "privacy.json"), CompanionConfiguration.ApiBaseUrl);
    internal static string Notice => (CompanionConfiguration.LocalTest ? "테스트 앱 안내\n이 앱은 UI 미리보기용입니다. 화면 정보는 이 기기에서만 확인하며 서버로 전송하지 않습니다.\n\n" : "") + Details;
    private sealed record Acceptance(int Version, string Server, DateTimeOffset AcceptedAtUtc);
    internal bool Accepted {
        get {
            try {
                var value = JsonSerializer.Deserialize<Acceptance>(File.ReadAllText(file));
                return value is { Version: Version } && value.Server == server && value.AcceptedAtUtc != default;
            } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return false; }
        }
    }
    internal void Accept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(new Acceptance(Version, server, DateTimeOffset.UtcNow)));
        File.Move(file + ".tmp", file, true);
    }
}

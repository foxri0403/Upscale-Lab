using System.Reflection;

namespace UpscaleLab.Companion;

internal static class CompanionConfiguration
{
    internal static bool LocalTest => Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(a => a.Key == "LocalTest" && a.Value == "true");
    internal static string AppName => LocalTest ? "Upscale Lab Connect (Local Test)" : "Upscale Lab Connect";
    internal static string ServerId => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(ApiBaseUrl)))[..12];
    internal static string WebDevicesUrl => (LocalTest || new Uri(ApiBaseUrl).IsLoopback ? "http://localhost:5173" : ApiBaseUrl) + "/app/devices";
    internal static string ApiBaseUrl => AgentApi.NormalizeServer(
        Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "DefaultApiBaseUrl").Value ?? "");

    internal static bool CanRestore(Credentials saved, string server) =>
        saved.Remember && saved.Session != null && saved.Server == server;
}

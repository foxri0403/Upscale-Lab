using System.Net.Http.Headers;

namespace UpscaleLab.Infrastructure.Upscaling;

internal static class RunPodCancellation
{
    public static async Task TryCancelAsync(HttpClient client, string baseUrl, string endpoint, string remoteId, string key)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/{endpoint}/cancel/{Uri.EscapeDataString(remoteId)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await client.SendAsync(request, timeout.Token);
        }
        catch { /* The provider TTL bounds work if the cancellation request cannot reach it. */ }
    }
}

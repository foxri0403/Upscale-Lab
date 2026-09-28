using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Upscaling;

namespace UpscaleLab.Infrastructure.Upscaling;

public sealed class ReplicateUpscaleService(HttpClient httpClient, ReplicateOptions options) : IUpscaleService
{
    public async Task<UpscalePrediction> CreatePredictionAsync(
        UpscaleRequest request,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var model = string.IsNullOrWhiteSpace(request.Model) ? options.Model : request.Model;
        var path = $"models/{model.Trim('/')}/predictions";
        using var message = CreateRequest(HttpMethod.Post, path);
        message.Content = JsonContent.Create(new
        {
            input = new
            {
                image = request.InputUrl,
                scale = request.Scale
            }
        });

        using var response = await httpClient.SendAsync(message, cancellationToken);
        return await ReadResponseAsync(response, cancellationToken);
    }

    public async Task<UpscalePrediction> GetPredictionAsync(
        string predictionId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var message = CreateRequest(HttpMethod.Get, $"predictions/{Uri.EscapeDataString(predictionId)}");
        using var response = await httpClient.SendAsync(message, cancellationToken);
        return await ReadResponseAsync(response, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var message = new HttpRequestMessage(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiToken);
        message.Headers.Add("Prefer", "wait=5");
        return message;
    }

    private static async Task<UpscalePrediction> ReadResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Replicate 요청이 실패했습니다. HTTP {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var output = root.TryGetProperty("output", out var outputElement)
            ? ReadOutput(outputElement)
            : null;

        return new UpscalePrediction(
            root.GetProperty("id").GetString() ?? string.Empty,
            root.GetProperty("status").GetString() ?? "unknown",
            output,
            root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null
                ? error.ToString()
                : null);
    }

    private static string? ReadOutput(JsonElement output)
    {
        return output.ValueKind switch
        {
            JsonValueKind.String => output.GetString(),
            JsonValueKind.Array when output.GetArrayLength() > 0 => output[0].GetString(),
            _ => null
        };
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.ApiToken))
        {
            throw new ConfigurationException("Replicate:ApiToken 설정이 필요합니다.");
        }
    }
}

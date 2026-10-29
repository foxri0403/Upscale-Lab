
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace UpscaleLab.Infrastructure.Upscaling;

public sealed record RunPodS3Result(int Width, int Height);

public sealed class RunPodS3UpscaleService(
    HttpClient httpClient,
    RunPodOptions options)
{
    public async Task<RunPodS3Result> UpscaleAsync(
        string inputUrl,
        string outputUrl,
        int scale,
        CancellationToken cancellationToken,
        int? targetLongEdge = null)
    {
        if (scale is not (2 or 4))
            throw new ArgumentException("Scale must be 2 or 4.");

        if (string.IsNullOrWhiteSpace(options.ApiKey) ||
            string.IsNullOrWhiteSpace(options.EndpointId))
            throw new InvalidOperationException(
                "RunPod configuration is missing.");

        if (options.TimeoutSeconds <= 0)
            throw new InvalidOperationException(
                "Invalid RunPod timeout.");

        using var deadline =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        deadline.CancelAfter(
            TimeSpan.FromSeconds(options.TimeoutSeconds));

        var token = deadline.Token;
        var baseUrl = options.ApiBaseUrl.TrimEnd('/');
        var endpoint = Uri.EscapeDataString(options.EndpointId);

        var runUrl = $"{baseUrl}/{endpoint}/run";

        using var request = new HttpRequestMessage(
            HttpMethod.Post, runUrl);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer", options.ApiKey);

        request.Content = JsonContent.Create(new
        {
            input = new
            {
                operation = "upscale_s3",
                scale,
                target_long_edge = targetLongEdge,
                input_url = inputUrl,
                output_url = outputUrl
            }
        });

        using var response =
            await httpClient.SendAsync(request, token);

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(token));

        var jobId = document.RootElement
            .GetProperty("id").GetString();

        if (string.IsNullOrWhiteSpace(jobId))
            throw new InvalidOperationException(
                "RunPod job ID is missing.");

        var statusUrl =
            $"{baseUrl}/{endpoint}/status/{Uri.EscapeDataString(jobId)}";

        while (true)
        {
            token.ThrowIfCancellationRequested();

            using var statusRequest =
                new HttpRequestMessage(HttpMethod.Get, statusUrl);

            statusRequest.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer", options.ApiKey);

            using var statusResponse =
                await httpClient.SendAsync(statusRequest, token);

            statusResponse.EnsureSuccessStatusCode();

            using var statusDocument = JsonDocument.Parse(
                await statusResponse.Content.ReadAsStringAsync(token));

            var result = statusDocument.RootElement;
            var status = result.GetProperty("status").GetString();

            if (status == "COMPLETED")
            {
                var output = result.GetProperty("output");

                if (output.GetProperty("status").GetString()
                    != "completed")
                    throw new InvalidOperationException(
                        "RunPod worker did not complete successfully.");

                if (output.GetProperty("transfer").GetString()
                    != "s3")
                    throw new InvalidOperationException(
                        "Unexpected transfer mode.");

                return new RunPodS3Result(
                    output.GetProperty("width").GetInt32(),
                    output.GetProperty("height").GetInt32());
            }

            if (status is "FAILED" or "CANCELLED" or "TIMED_OUT")
                throw new InvalidOperationException(
                    $"RunPod job ended with status: {status}");

            if (status is not ("IN_QUEUE" or "IN_PROGRESS"))
                throw new InvalidOperationException(
                    $"Unexpected RunPod status: {status}");

            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
    }
}

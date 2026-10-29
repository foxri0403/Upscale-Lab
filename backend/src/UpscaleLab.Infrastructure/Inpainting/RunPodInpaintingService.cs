using ImageMagick;
using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using UpscaleLab.Application.Storage;

namespace UpscaleLab.Infrastructure.Inpainting;

public sealed record InpaintingResult(
    string ObjectKey,
    string DownloadUrl
);

public sealed record ValidatedInpaintingInput(
    byte[] ImageBytes,
    byte[] MaskBytes
);

public sealed class RunPodInpaintingService(
    HttpClient httpClient,
    RunPodInpaintingOptions options,
    IStorageService storage)
{
    private const int MaxInputBytes = 16 * 1024 * 1024;
    private const long MaxPixels = 20_000_000;
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(30);

    public async Task<InpaintingResult> InpaintAsync(
        Stream image,
        Stream mask,
        CancellationToken cancellationToken)
    {
        var validated = await ValidateAsync(
            image, mask, cancellationToken);

        return await InpaintValidatedAsync(
            validated, cancellationToken);
    }

    public static async Task<ValidatedInpaintingInput> ValidateAsync(
        Stream image,
        Stream mask,
        CancellationToken cancellationToken)
    {
        using var imageData = await ReadPngAsync(
            image, cancellationToken);
        using var maskData = await ReadPngAsync(
            mask, cancellationToken);

        if (GetDimensions(imageData) != GetDimensions(maskData))
            throw new InvalidDataException(
                "Image and mask dimensions must match.");

        ValidateMask(maskData);

        return new ValidatedInpaintingInput(
            imageData.ToArray(),
            maskData.ToArray());
    }

    public async Task<InpaintingResult> InpaintValidatedAsync(
        ValidatedInpaintingInput validated,
        CancellationToken cancellationToken,
        Func<string, CancellationToken, Task>? onRunPodJobCreated = null)
    {
        if (!options.Enabled ||
            string.IsNullOrWhiteSpace(options.ApiKey) ||
            string.IsNullOrWhiteSpace(options.EndpointId))
        {
            throw new InvalidOperationException(
                "RunPod Inpainting is not configured.");
        }

        using var deadline =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        deadline.CancelAfter(TimeSpan.FromSeconds(
            Math.Clamp(options.TimeoutSeconds, 60, 1500)));

        var token = deadline.Token;

        using var imageData = new MemoryStream(
            validated.ImageBytes, writable: false);
        using var maskData = new MemoryStream(
            validated.MaskBytes, writable: false);

        var prefix = $"inpainting/{Guid.NewGuid():N}";
        var imageKey = $"{prefix}/input.png";
        var maskKey = $"{prefix}/mask.png";
        var outputKey = $"{prefix}/output.png";

        var success = false;

        try
        {
            imageData.Position = 0;
            await storage.UploadAsync(
                imageData, imageKey, "image/png", token);

            maskData.Position = 0;
            await storage.UploadAsync(
                maskData, maskKey, "image/png", token);

            var imageUrl = storage.CreateDownloadUrl(
                imageKey, UrlLifetime);

            var maskUrl = storage.CreateDownloadUrl(
                maskKey, UrlLifetime);

            var outputUrl = storage.CreateUploadUrl(
                outputKey, "image/png", UrlLifetime);

            var endpoint = Uri.EscapeDataString(options.EndpointId);
            var baseUrl = $"https://api.runpod.ai/v2/{endpoint}";

            using var submit = new HttpRequestMessage(
                HttpMethod.Post, $"{baseUrl}/run")
            {
                Content = JsonContent.Create(new
                {
                    input = new
                    {
                        operation = "background_inpainting",
                        image_url = imageUrl,
                        mask_url = maskUrl,
                        output_url = outputUrl
                    }
                })
            };

            submit.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer", options.ApiKey);

            using var submitResponse =
                await httpClient.SendAsync(submit, token);

            submitResponse.EnsureSuccessStatusCode();

            using var submitJson = JsonDocument.Parse(
                await submitResponse.Content.ReadAsStringAsync(token));

            var jobId = submitJson.RootElement
                .GetProperty("id").GetString();

            if (string.IsNullOrWhiteSpace(jobId))
                throw new InvalidDataException(
                    "RunPod job ID is missing.");

            if (onRunPodJobCreated is not null)
            {
                await onRunPodJobCreated(
                    jobId, CancellationToken.None);
            }

            var statusUrl =
                $"{baseUrl}/status/{Uri.EscapeDataString(jobId)}";

            while (true)
            {
                token.ThrowIfCancellationRequested();

                using var statusRequest = new HttpRequestMessage(
                    HttpMethod.Get, statusUrl);

                statusRequest.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer", options.ApiKey);

                using var response =
                    await httpClient.SendAsync(statusRequest, token);

                response.EnsureSuccessStatusCode();

                using var document = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(token));

                var root = document.RootElement;
                var status = root.GetProperty("status").GetString();

                if (status == "COMPLETED")
                {
                    var output = root.GetProperty("output");

                    if (output.GetProperty("status").GetString()
                        != "completed")
                    {
                        throw new InvalidOperationException(
                            "Inpainting worker failed.");
                    }

                    break;
                }

                if (status is "FAILED" or "CANCELLED" or "TIMED_OUT")
                {
                    throw new InvalidOperationException(
                        $"Inpainting job ended with status: {status}");
                }

                if (status is not ("IN_QUEUE" or "IN_PROGRESS"))
                    throw new InvalidDataException(
                        "Unexpected RunPod job status.");

                await Task.Delay(TimeSpan.FromSeconds(3), token);
            }

            // Verify that RunPod really uploaded the output.
            await using (var check = new MemoryStream())
            {
                await storage.DownloadAsync(outputKey, check, token);

                if (check.Length < 24 ||
                    check.Length > 64L * 1024 * 1024)
                {
                    throw new InvalidDataException(
                        "Invalid inpainting output size.");
                }

                check.Position = 0;
                _ = GetDimensions(check);
            }

            success = true;

            return new InpaintingResult(
                outputKey,
                storage.CreateDownloadUrl(
                    outputKey, TimeSpan.FromMinutes(15)));
        }
        finally
        {
            foreach (var key in success
                ? new[] { imageKey, maskKey }
                : new[] { imageKey, maskKey, outputKey })
            {
                try
                {
                    await storage.DeleteAsync(
                        key, CancellationToken.None);
                }
                catch
                {
                    // Temporary files also need S3 lifecycle cleanup.
                }
            }
        }
    }

    private static async Task<MemoryStream> ReadPngAsync(
        Stream source,
        CancellationToken ct)
    {
        var result = new MemoryStream();
        var buffer = new byte[64 * 1024];

        try
        {
            while (true)
            {
                var count = await source.ReadAsync(buffer, ct);

                if (count == 0)
                    break;

                if (result.Length + count > MaxInputBytes)
                    throw new InvalidDataException(
                        "Image exceeds 16 MB.");

                await result.WriteAsync(
                    buffer.AsMemory(0, count), ct);
            }

            result.Position = 0;
            var dimensions = GetDimensions(result);

            result.Position = 0;

            try
            {
                using var decoded = new MagickImage(result);

                if (decoded.Format != MagickFormat.Png ||
                    decoded.Width != dimensions.Width ||
                    decoded.Height != dimensions.Height)
                {
                    throw new InvalidDataException(
                        "PNG image dimensions are inconsistent.");
                }
            }
            catch (MagickException ex)
            {
                throw new InvalidDataException(
                    "PNG image could not be decoded.", ex);
            }

            result.Position = 0;
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static void ValidateMask(MemoryStream maskData)
    {
        maskData.Position = 0;

        try
        {
            using var maskImage = new MagickImage(maskData);
            using var pixels = maskImage.GetPixels();

            var rgb = pixels.ToByteArray("RGB");

            if (rgb is null || rgb.Length == 0 ||
                rgb.Length % 3 != 0)
            {
                throw new InvalidDataException(
                    "Invalid mask pixel data.");
            }

            long selected = 0;
            long total = rgb.Length / 3;

            for (var i = 0; i < rgb.Length; i += 3)
            {
                var brightness =
                    (rgb[i] + rgb[i + 1] + rgb[i + 2]) / 3;

                if (brightness >= 128)
                    selected++;
            }

            if (selected == 0)
                throw new InvalidDataException(
                    "Mask contains no restoration area.");

            if (selected == total)
                throw new InvalidDataException(
                    "Mask covers the entire image.");
        }
        catch (MagickException ex)
        {
            throw new InvalidDataException(
                "Mask pixels could not be decoded.", ex);
        }
        finally
        {
            maskData.Position = 0;
        }
    }

    private static (int Width, int Height) GetDimensions(
        MemoryStream stream)
    {
        var bytes = stream.ToArray();

        ReadOnlySpan<byte> pngSignature =
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

        if (bytes.Length < 24 ||
            !bytes.AsSpan(0, 8).SequenceEqual(pngSignature) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            throw new InvalidDataException(
                "Only PNG images are supported.");
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(
            bytes.AsSpan(16, 4));

        var height = BinaryPrimitives.ReadInt32BigEndian(
            bytes.AsSpan(20, 4));

        if (width <= 0 || height <= 0 ||
            (long)width * height > MaxPixels)
        {
            throw new InvalidDataException(
                "Invalid image dimensions.");
        }

        return (width, height);
    }
}

using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Infrastructure.Inpainting;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/inpainting")]
public sealed class InpaintingController(
    RunPodInpaintingService inpaintingService,
    InpaintingQuotaService quotaService,
    RunPodInpaintingOptions options,
    ILogger<InpaintingController> logger) : ControllerBase
{
    // Temporary single-instance limit for development testing.
    private static readonly SemaphoreSlim ProcessingGate = new(1, 1);

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> Inpaint(
        IFormFile? image,
        IFormFile? mask,
        CancellationToken cancellationToken)
    {
        // Keep the new paid GPU endpoint unavailable in production
        // until persistent job tracking and per-user quotas are ready.
        if (!options.Enabled)
            return NotFound();

        var userId = User.GetRequiredUserId();

        const long maxFileBytes = 10L * 1024 * 1024;

        if (image is null || mask is null ||
            image.Length is <= 0 or > maxFileBytes ||
            mask.Length is <= 0 or > maxFileBytes)
        {
            return BadRequest(new
            {
                message = "Image and mask must each be between 1 byte and 10 MB."
            });
        }

        if (!string.Equals(image.ContentType, "image/png",
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(mask.ContentType, "image/png",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "Both files must be PNG images."
            });
        }

        if (!await ProcessingGate.WaitAsync(0, cancellationToken))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = "An inpainting test is already running." });
        }

        try
        {
            await using var imageStream = image.OpenReadStream();
            await using var maskStream = mask.OpenReadStream();

            // Validate before consuming a daily GPU request.
            var validated = await RunPodInpaintingService.ValidateAsync(
                imageStream,
                maskStream,
                cancellationToken);

            var job = await quotaService.ReserveAsync(
                userId, cancellationToken);

            if (job is null)
            {
                return StatusCode(
                    StatusCodes.Status429TooManyRequests,
                    new { message = "Daily inpainting limit reached." });
            }

            try
            {
                var result = await inpaintingService.InpaintValidatedAsync(
                    validated,
                    cancellationToken,
                    async (runPodJobId, _) =>
                    {
                        await quotaService.SaveRunPodJobIdAsync(
                            job.Id,
                            runPodJobId,
                            CancellationToken.None);
                    });

                await quotaService.CompleteAsync(
                    job.Id, result.ObjectKey, CancellationToken.None);

                return Ok(new
                {
                    jobId = job.Id,
                    objectKey = result.ObjectKey,
                    downloadUrl = result.DownloadUrl
                });
            }
            catch (Exception ex)
            {
                try
                {
                    await quotaService.FailAsync(
                        job.Id,
                        ex.GetType().Name,
                        CancellationToken.None);
                }
                catch (Exception recordError)
                {
                    logger.LogError(
                        recordError,
                        "Failed to record inpainting job failure.");
                }

                if (ex is InvalidDataException)
                {
                    logger.LogWarning(
                        "RunPod returned invalid inpainting data.");

                    return StatusCode(
                        StatusCodes.Status502BadGateway,
                        new { message = "Inpainting processing failed." });
                }

                if (ex is OperationCanceledException &&
                    cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation(
                        "Inpainting request was cancelled: {JobId}",
                        job.Id);

                    return new EmptyResult();
                }

                throw;
            }
        }
        catch (InvalidDataException)
        {
            return BadRequest(new
            {
                message = "Invalid image or mask data."
            });
        }
        catch (Exception ex) when (
            ex is HttpRequestException or
                  JsonException or
                  InvalidOperationException or
                  Amazon.S3.AmazonS3Exception)
        {
            logger.LogWarning(
                "Inpainting request failed: {ExceptionType}",
                ex.GetType().Name);

            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "Inpainting processing failed." });
        }
        finally
        {
            ProcessingGate.Release();
        }
    }
}

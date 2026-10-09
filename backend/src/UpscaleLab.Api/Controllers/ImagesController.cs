using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Api.Models;
using UpscaleLab.Application.Images;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/images")]
public sealed class ImagesController(IImageService imageService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ImageResponse>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await imageService.GetAllAsync(User.GetRequiredUserId(), cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ImageResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await imageService.GetAsync(User.GetRequiredUserId(), id, cancellationToken));
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<ActionResult<ImageResponse>> Upload(
        [FromForm] UploadImageForm form,
        CancellationToken cancellationToken)
    {
        if (form.File.Length <= 0)
        {
            return ValidationProblem("빈 파일은 업로드할 수 없습니다.");
        }

        if (!ImageFormatPolicy.TryResolveContentType(
                form.File.FileName,
                form.File.ContentType,
                out var contentType))
        {
            ModelState.AddModelError(nameof(form.File), ImageFormatPolicy.SupportedFormatsMessage);
            return ValidationProblem(ModelState);
        }

        await using var stream = form.File.OpenReadStream();
        var command = new ImageUploadCommand(
            stream,
            form.File.FileName,
            contentType,
            form.OriginalWidth,
            form.OriginalHeight);
        var response = await imageService.UploadAsync(User.GetRequiredUserId(), command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("{id:guid}/download-url")]
    public async Task<ActionResult<DownloadUrlResponse>> CreateDownloadUrl(
        Guid id,
        CancellationToken cancellationToken)
    {
        return Ok(await imageService.CreateDownloadUrlAsync(User.GetRequiredUserId(), id, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await imageService.DeleteAsync(User.GetRequiredUserId(), id, cancellationToken);
        return NoContent();
    }
}

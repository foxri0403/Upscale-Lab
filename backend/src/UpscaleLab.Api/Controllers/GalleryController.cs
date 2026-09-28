using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Application.Gallery;
using UpscaleLab.Application.Images;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Route("api/gallery")]
public sealed class GalleryController(IGalleryService galleryService) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GalleryPostResponse>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await galleryService.GetAllAsync(User.GetOptionalUserId(), cancellationToken));
    }

    [AllowAnonymous]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GalleryPostDetailResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await galleryService.GetAsync(id, User.GetOptionalUserId(), cancellationToken));
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<GalleryPostResponse>> Create(
        CreateGalleryPostRequest request,
        CancellationToken cancellationToken)
    {
        var response = await galleryService.CreateAsync(User.GetRequiredUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await galleryService.DeletePostAsync(User.GetRequiredUserId(), id, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpPost("{id:guid}/comments")]
    public async Task<ActionResult<CommentResponse>> AddComment(
        Guid id,
        CreateCommentRequest request,
        CancellationToken cancellationToken)
    {
        var response = await galleryService.AddCommentAsync(User.GetRequiredUserId(), id, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [Authorize]
    [HttpPost("{id:guid}/likes")]
    public async Task<IActionResult> Like(Guid id, CancellationToken cancellationToken)
    {
        await galleryService.LikeAsync(User.GetRequiredUserId(), id, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpDelete("{id:guid}/likes")]
    public async Task<IActionResult> Unlike(Guid id, CancellationToken cancellationToken)
    {
        await galleryService.UnlikeAsync(User.GetRequiredUserId(), id, cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("{id:guid}/download-url")]
    public async Task<ActionResult<DownloadUrlResponse>> CreateDownloadUrl(
        Guid id,
        CancellationToken cancellationToken)
    {
        return Ok(await galleryService.CreateDownloadUrlAsync(id, cancellationToken));
    }
}

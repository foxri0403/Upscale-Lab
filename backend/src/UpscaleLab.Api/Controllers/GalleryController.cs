using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Application.Gallery;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Projects;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Route("api/gallery")]
public sealed class GalleryController(IGalleryService galleryService) : ControllerBase
{
    [Authorize(Policy = "AccountSession")]
    [HttpPost("{id:guid}/import")]
    public async Task<ActionResult<ProjectDetailResponse>> Import(Guid id,
        [FromServices] IGalleryImportService imports, CancellationToken cancellationToken)
    {
        var project = await imports.ImportAsync(User.GetRequiredUserId(), id, cancellationToken);
        return Created($"/api/projects/{project.Id}", project);
    }

    [AllowAnonymous]
    [HttpGet("{id:guid}/thumbnail")]
    public async Task<IActionResult> Thumbnail(Guid id,
        [FromServices] IGalleryThumbnailService thumbnails, CancellationToken cancellationToken)
    {
        var bytes = await thumbnails.GetAsync(id, User.GetOptionalUserId(), cancellationToken);
        if (bytes is null) return Redirect((await galleryService.CreatePreviewUrlAsync(
            id, User.GetOptionalUserId(), cancellationToken)).Url);
        Response.Headers.CacheControl = "private, max-age=300";
        return File(bytes, "image/jpeg");
    }
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GalleryPostResponse>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? tag,
        [FromQuery] string[]? tags,
        [FromQuery] string[]? platformTags,
        [FromQuery] string? sort,
        [FromQuery] bool untagged,
        CancellationToken cancellationToken,
        [FromQuery] int limit = 100)
    {
        return Ok(await galleryService.GetAllAsync(User.GetOptionalUserId(), search, tag, cancellationToken, tags,
            new ExploreQuery(search, sort, tags, platformTags, untagged, limit)));
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
    [HttpPatch("{id:guid}/tag")]
    public async Task<ActionResult<GalleryPostResponse>> UpdateTag(
        Guid id,
        UpdateGalleryPostTagRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await galleryService.UpdateTagAsync(
            User.GetRequiredUserId(),
            id,
            request,
            cancellationToken));
    }

    [Authorize]
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GalleryPostResponse>> Update(
        Guid id, UpdateGalleryPostRequest request, CancellationToken cancellationToken)
    {
        return Ok(await galleryService.UpdateAsync(
            User.GetRequiredUserId(), id, request, cancellationToken));
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

    [Authorize(Policy = "AccountSession")]
    [HttpPost("{id:guid}/likes")]
    public async Task<ActionResult<GalleryEngagement>> Like(Guid id, CancellationToken cancellationToken) =>
        Ok(await galleryService.SetLikeAsync(User.GetRequiredUserId(), id, true, cancellationToken));

    [Authorize(Policy = "AccountSession")]
    [HttpDelete("{id:guid}/likes")]
    public async Task<ActionResult<GalleryEngagement>> Unlike(Guid id, CancellationToken cancellationToken) =>
        Ok(await galleryService.SetLikeAsync(User.GetRequiredUserId(), id, false, cancellationToken));

    [AllowAnonymous]
    [HttpGet("{id:guid}/preview-url")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<DownloadUrlResponse>> PreviewUrl(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await galleryService.CreatePreviewUrlAsync(id, User.GetOptionalUserId(), cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("{id:guid}/download-url")]
    public async Task<ActionResult<DownloadUrlResponse>> CreateDownloadUrl(
        Guid id,
        CancellationToken cancellationToken,
        [FromQuery] bool countDownload = true)
    {
        return Ok(await galleryService.CreateDownloadUrlAsync(id, User.GetOptionalUserId(), cancellationToken, countDownload));
    }

    [AllowAnonymous]
    [HttpPost("{id:guid}/download-complete")]
    public async Task<ActionResult<GalleryDownloadCountResponse>> RecordDownload(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await galleryService.RecordDownloadAsync(id, User.GetOptionalUserId(), cancellationToken));
    }
}

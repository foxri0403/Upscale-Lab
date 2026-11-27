using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UpscaleLab.Api.Authentication;
using UpscaleLab.Application.Images;

namespace UpscaleLab.Api.Controllers;

[ApiController]
[Authorize(Policy = "AccountSession")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/web-images")]
public sealed class WebImagesController(IWebpImageService images) : ControllerBase
{
    [HttpGet("projects/{id:guid}")]
    public Task<WebpImageView> Project(Guid id, CancellationToken ct) =>
        images.GetAsync(WebpImageKind.Project, id, User.GetRequiredUserId(), ct);

    [HttpGet("wallpapers/{id:guid}")]
    public Task<WebpImageView> Wallpaper(Guid id, CancellationToken ct) =>
        images.GetAsync(WebpImageKind.Wallpaper, id, User.GetRequiredUserId(), ct);
}
